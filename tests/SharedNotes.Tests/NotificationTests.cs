using System.Net;
using System.Text.Json;

namespace SharedNotes.Tests;

public record Notification(int NotificationId, string Type, string Message, int? NoteId, int? TeamId,
    int ActorEmployeeId, string ActorName, DateTime CreatedAt, bool IsRead);

public class NotificationTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static async Task<List<Notification>> ListAsync(HttpClient c, bool unreadOnly = false) =>
        await (await c.GetAsync($"/api/notifications?unreadOnly={unreadOnly.ToString().ToLower()}")).ReadAsync<List<Notification>>();

    private static async Task<int> UnreadAsync(HttpClient c) =>
        (await (await c.GetAsync("/api/notifications/unread-count")).ReadAsync<JsonElement>()).GetProperty("count").GetInt32();

    [Fact]
    public async Task EditingSomeoneElsesNote_NotifiesItsCreator()
    {
        var alice = _factory.ClientFor(1);
        var bob = _factory.ClientFor(2);
        var note = await alice.CreateNoteAsync("Roadmap");   // an "All" note, so Bob may edit it
        await bob.PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(note, note.Version, title: "Roadmap v2"));

        var inbox = await ListAsync(alice);
        var n = Assert.Single(inbox);
        Assert.Equal("note-edited", n.Type);
        Assert.Equal("Bob Martinez edited your note “Roadmap v2”", n.Message);
        Assert.Equal(note.NoteId, n.NoteId);
        Assert.Equal(2, n.ActorEmployeeId);
        Assert.False(n.IsRead);
        Assert.Empty(await ListAsync(bob));   // the editor is not notified about their own action
    }

    [Fact]
    public async Task EditingYourOwnNote_NotifiesNobody()
    {
        var alice = _factory.ClientFor(1);
        var note = await alice.CreateNoteAsync();
        await alice.PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(note, note.Version, title: "again"));
        Assert.Empty(await ListAsync(alice));
    }

    [Fact]
    public async Task SharingANoteWithATeam_NotifiesOtherTeamMembersOnly()
    {
        var alice = _factory.ClientFor(1);   // member of Engineering and Marketing
        var note = await alice.CreateNoteAsync("Eng kickoff", visibility: "Team", teamId: 1);

        var bob = await ListAsync(_factory.ClientFor(2));   // Engineering member
        Assert.Equal("note-shared", Assert.Single(bob).Type);
        Assert.Equal(note.NoteId, bob[0].NoteId);
        Assert.Contains("shared “Eng kickoff” with Engineering", bob[0].Message);

        Assert.Empty(await ListAsync(_factory.ClientFor(3)));   // Marketing only
        Assert.Empty(await ListAsync(_factory.ClientFor(4)));   // no team
        Assert.Empty(await ListAsync(alice));                   // the author
    }

    [Fact]
    public async Task PublicAndPrivateNotes_DoNotNotifyAnyoneOnCreate()
    {
        var alice = _factory.ClientFor(1);
        await alice.CreateNoteAsync(visibility: "All");
        await alice.CreateNoteAsync(visibility: "Private");
        foreach (var id in new[] { 2, 3, 4 }) Assert.Empty(await ListAsync(_factory.ClientFor(id)));
    }

    [Fact]
    public async Task BeingAddedToATeam_NotifiesTheNewMember()
    {
        await _factory.ClientFor(1).PostJsonAsync("/api/teams/1/members", new { employeeId = 4 });
        var n = Assert.Single(await ListAsync(_factory.ClientFor(4)));
        Assert.Equal("team-added", n.Type);
        Assert.Equal(1, n.TeamId);
        Assert.Contains("added you to the team Engineering", n.Message);
    }

    [Fact]
    public async Task UnreadCount_MarkRead_AndMarkAllRead()
    {
        var alice = _factory.ClientFor(1);
        var bob = _factory.ClientFor(2);
        for (var i = 0; i < 3; i++)
        {
            var note = await alice.CreateNoteAsync($"n{i}");
            await bob.PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(note, note.Version, title: $"edited {i}"));
        }
        Assert.Equal(3, await UnreadAsync(alice));

        var inbox = await ListAsync(alice);
        Assert.Equal(["edited 2", "edited 1", "edited 0"], inbox.Select(n => n.Message.Split('“', '”')[1]));   // newest first

        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync($"/api/notifications/{inbox[0].NotificationId}/read", null)).StatusCode);
        Assert.Equal(2, await UnreadAsync(alice));
        Assert.Equal(2, (await ListAsync(alice, unreadOnly: true)).Count);
        Assert.Equal(3, (await ListAsync(alice)).Count);   // read ones stay in the full list

        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync("/api/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, await UnreadAsync(alice));
        Assert.Empty(await ListAsync(alice, unreadOnly: true));
    }

    [Fact]
    public async Task NotificationsArePrivateToTheirRecipient()
    {
        var alice = _factory.ClientFor(1);
        var bob = _factory.ClientFor(2);
        var note = await alice.CreateNoteAsync();
        await bob.PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(note, note.Version, title: "x"));
        var id = (await ListAsync(alice)).Single().NotificationId;

        // Bob cannot mark Alice's notification as read; it looks like it doesn't exist.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/notifications/{id}/read", null)).StatusCode);
        Assert.Equal(1, await UnreadAsync(alice));

        // Bob's "read all" does not touch Alice's either.
        await bob.PostAsync("/api/notifications/read-all", null);
        Assert.Equal(1, await UnreadAsync(alice));
    }

    [Fact]
    public async Task Notifications_RequireSignIn()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.ClientFor(null).GetAsync("/api/notifications")).StatusCode);
    }

    [Fact]
    public async Task Notification_SurvivesDeletionOfItsNote_WithoutALink()
    {
        var alice = _factory.ClientFor(1);
        var bob = _factory.ClientFor(2);
        var note = await alice.CreateNoteAsync("Temporary");
        await bob.PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(note, note.Version, title: "Temporary 2"));
        await alice.DeleteAsync($"/api/notes/{note.NoteId}");

        var n = Assert.Single(await ListAsync(alice));
        Assert.Null(n.NoteId);
        Assert.Contains("Temporary 2", n.Message);
    }

    [Fact]
    public async Task CreatorWhoCanNoLongerReadTheNote_IsNotNotified()
    {
        // Bob creates an Engineering note, then is removed from Engineering by Alice (a team lead/admin).
        var bob = _factory.ClientFor(2);
        var alice = _factory.ClientFor(1);
        var note = await bob.CreateNoteAsync("Eng only", visibility: "Team", teamId: 1);
        await alice.DeleteAsync("/api/teams/1/members/2");

        var current = await (await alice.GetAsync($"/api/notes/{note.NoteId}")).ReadAsync<Note>();
        await alice.PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(current, current.Version, title: "Renamed secretly"));

        Assert.Empty(await ListAsync(bob));
    }

    [Fact]
    public async Task Recipient_ReceivesALiveNotificationEvent_AndOthersDoNot()
    {
        using var aliceStream = await SseReader.ConnectAsync(_factory.ClientFor(1));
        using var carolStream = await SseReader.ConnectAsync(_factory.ClientFor(3));
        var alice = _factory.ClientFor(1);
        var note = await alice.CreateNoteAsync();
        // Creating an All note fires note-changed for everyone; drain it first.
        Assert.Equal("note-changed", (await aliceStream.NextAsync())?.Type);
        Assert.Equal("note-changed", (await carolStream.NextAsync())?.Type);

        await _factory.ClientFor(2).PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(note, note.Version, title: "edited"));

        // Alice sees note-changed, then her private notification event.
        SseEvent? ev;
        do { ev = await aliceStream.NextAsync(); } while (ev is not null && ev.Type != "notification");
        Assert.NotNull(ev);
        Assert.Equal(2, ev.Data.GetProperty("actorId").GetInt32());

        // Carol only gets the public note-changed event, never Alice's notification.
        var carolEvents = new List<string>();
        for (SseEvent? e; (e = await carolStream.NextAsync(TimeSpan.FromMilliseconds(400))) is not null;) carolEvents.Add(e.Type);
        Assert.DoesNotContain("notification", carolEvents);
    }
}
