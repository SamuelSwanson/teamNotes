using System.Net;

namespace SharedNotes.Tests;

public record ShoutOut(int ShoutOutId, string Kind, string Message, string Status, int TeamId, string TeamName, int? NoteId,
    string? NoteTitle, int SenderEmployeeId, string SenderName, DateTime? ResolvedAt, string? ResolvedByName, string? Response);

// Seed: Alice (admin) is in Engineering + Marketing, Bob in Engineering, Carol in Marketing, Dave in no team.
public class ShoutOutTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient As(int id) => _factory.ClientFor(id);

    private static Task<HttpResponseMessage> Send(HttpClient c, int teamId, object body) => c.PostJsonAsync($"/api/teams/{teamId}/shoutouts", body);

    private static async Task<List<ShoutOut>> ListAsync(HttpClient c, string query = "") =>
        await (await c.GetAsync("/api/shoutouts" + query)).ReadAsync<List<ShoutOut>>();

    private static async Task<List<Notification>> NotificationsAsync(HttpClient c) =>
        await (await c.GetAsync("/api/notifications")).ReadAsync<List<Notification>>();

    [Fact]
    public async Task ShoutingOutToAnotherTeam_NotifiesItsMembers_AndAppearsInTheirInbox()
    {
        // Carol (Marketing) asks Engineering to review something. She is not in that team.
        var response = await Send(As(3), 1, new { kind = "Review", message = "Could you check our launch plan?" });
        var created = await response.ReadAsync<ShoutOut>(HttpStatusCode.Created);
        Assert.Equal("Open", created.Status);
        Assert.Equal("Engineering", created.TeamName);
        Assert.Equal(3, created.SenderEmployeeId);

        foreach (var memberId in new[] { 1, 2 })   // Alice and Bob are Engineering members
        {
            var inbox = await ListAsync(As(memberId));
            Assert.Equal(created.ShoutOutId, Assert.Single(inbox).ShoutOutId);
            var note = Assert.Single(await NotificationsAsync(As(memberId)));
            Assert.Equal("shout-out", note.Type);
            Assert.Contains("Carol Singh asked Engineering to review something: Could you check our launch plan?", note.Message);
        }

        Assert.Empty(await ListAsync(As(4)));                      // Dave is in no team
        Assert.Empty(await NotificationsAsync(As(3)));             // the sender is not notified
        Assert.Single(await ListAsync(As(3), "?box=sent"));
    }

    [Fact]
    public async Task ShoutOut_ForANote_NamesIt_AndRecipientsCanOpenIt()
    {
        var note = await As(1).CreateNoteAsync("Launch plan", visibility: "All");
        var created = await (await Send(As(3), 1, new { kind = "Edit", message = "Tighten the intro", noteId = note.NoteId }))
            .ReadAsync<ShoutOut>(HttpStatusCode.Created);
        Assert.Equal(note.NoteId, created.NoteId);
        Assert.Equal("Launch plan", created.NoteTitle);

        var bobNotice = (await NotificationsAsync(As(2))).First(n => n.Type == "shout-out");
        Assert.Contains("asked Engineering to edit “Launch plan”", bobNotice.Message);
        Assert.Equal(note.NoteId, bobNotice.NoteId);
        Assert.Equal(HttpStatusCode.OK, (await As(2).GetAsync($"/api/notes/{bobNotice.NoteId}")).StatusCode);
    }

    [Fact]
    public async Task ShoutOut_CannotPointAtANoteTheTeamCannotRead()
    {
        // Alice's private note, and a Marketing-only note: Engineering cannot read either.
        var privateNote = await As(1).CreateNoteAsync(visibility: "Private");
        var marketingNote = await As(1).CreateNoteAsync(visibility: "Team", teamId: 2);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(As(1), 1, new { kind = "Review", message = "x", noteId = privateNote.NoteId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(As(1), 1, new { kind = "Review", message = "x", noteId = marketingNote.NoteId })).StatusCode);

        // A team note of the *target* team is fine.
        var engineeringNote = await As(1).CreateNoteAsync(visibility: "Team", teamId: 1);
        Assert.Equal(HttpStatusCode.Created, (await Send(As(1), 1, new { kind = "Review", message = "x", noteId = engineeringNote.NoteId })).StatusCode);
    }

    [Fact]
    public async Task ShoutOut_CannotPointAtANoteTheSenderCannotRead()
    {
        // Seed note 1 is Alice's private note: Bob cannot read it, so he cannot reference it.
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(As(2), 1, new { kind = "Review", message = "x", noteId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(As(2), 1, new { kind = "Review", message = "x", noteId = 9999 })).StatusCode);
    }

    [Theory]
    [InlineData("""{"kind":"Review","message":""}""")]
    [InlineData("""{"kind":"Review"}""")]
    [InlineData("""{"message":"hello"}""")]
    [InlineData("""{"kind":"Shout","message":"hello"}""")]
    public async Task InvalidRequests_AreRejected(string json)
    {
        var response = await As(3).PostAsync("/api/teams/1/shoutouts", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TooLongMessage_AndUnknownTeam_AreRejected()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(As(3), 1, new { kind = "Info", message = new string('x', 501) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(As(3), 999, new { kind = "Info", message = "hi" })).StatusCode);
    }

    [Fact]
    public async Task Resolving_NotifiesTheSenderWithTheResponse_AndMovesItOutOfTheOpenInbox()
    {
        var created = await (await Send(As(3), 1, new { kind = "Answer", message = "Which database should we use?" })).ReadAsync<ShoutOut>(HttpStatusCode.Created);

        var resolved = await (await As(2).PostJsonAsync($"/api/shoutouts/{created.ShoutOutId}/resolve", new { response = "SQLite is fine for now." }))
            .ReadAsync<ShoutOut>();
        Assert.Equal("Resolved", resolved.Status);
        Assert.Equal("Bob Martinez", resolved.ResolvedByName);
        Assert.Equal("SQLite is fine for now.", resolved.Response);

        var carolNotice = (await NotificationsAsync(As(3))).Single(n => n.Type == "shout-out-resolved");
        Assert.Equal("Bob Martinez resolved your request to Engineering: SQLite is fine for now.", carolNotice.Message);

        Assert.Empty(await ListAsync(As(1)));                                   // default status=open
        Assert.Single(await ListAsync(As(1), "?status=resolved"));
        Assert.Single(await ListAsync(As(1), "?status=all"));
    }

    [Fact]
    public async Task Resolve_RulesAreEnforced()
    {
        var created = await (await Send(As(3), 1, new { kind = "Review", message = "please" })).ReadAsync<ShoutOut>(HttpStatusCode.Created);
        var url = $"/api/shoutouts/{created.ShoutOutId}";

        Assert.Equal(HttpStatusCode.Forbidden, (await As(4).PostAsync($"{url}/resolve", null)).StatusCode);   // Dave: unrelated
        Assert.Equal(HttpStatusCode.NotFound, (await As(2).PostAsync("/api/shoutouts/9999/resolve", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await As(3).PostAsync($"{url}/resolve", null)).StatusCode);          // the sender may withdraw it
        Assert.Equal(HttpStatusCode.Conflict, (await As(2).PostAsync($"{url}/resolve", null)).StatusCode);    // already resolved
    }

    [Fact]
    public async Task ShoutOuts_AreOnlyVisibleToTheSenderAndTheTargetTeam()
    {
        var created = await (await Send(As(3), 1, new { kind = "Info", message = "FYI" })).ReadAsync<ShoutOut>(HttpStatusCode.Created);
        var url = $"/api/shoutouts/{created.ShoutOutId}";
        Assert.Equal(HttpStatusCode.OK, (await As(3).GetAsync(url)).StatusCode);    // sender
        Assert.Equal(HttpStatusCode.OK, (await As(2).GetAsync(url)).StatusCode);    // team member
        Assert.Equal(HttpStatusCode.Forbidden, (await As(4).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await As(3).GetAsync("/api/shoutouts?box=everything")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await As(3).GetAsync("/api/shoutouts?status=maybe")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.ClientFor(null).GetAsync("/api/shoutouts")).StatusCode);
    }

    [Fact]
    public async Task NoteTitle_IsHiddenFromPeopleWhoCanNoLongerReadTheNote()
    {
        var note = await As(1).CreateNoteAsync("Quarterly secrets", visibility: "All");
        var created = await (await Send(As(1), 1, new { kind = "Review", message = "look", noteId = note.NoteId })).ReadAsync<ShoutOut>(HttpStatusCode.Created);

        // The creator makes the note private afterwards; Bob (a recipient) can no longer read it.
        await As(1).PutJsonAsync($"/api/notes/{note.NoteId}", new
        {
            title = note.Title, content = note.Content, visibility = "Private", teamId = (int?)null, topicIds = new int[0], expectedVersion = note.Version,
        });

        var bobView = await (await As(2).GetAsync($"/api/shoutouts/{created.ShoutOutId}")).ReadAsync<ShoutOut>();
        Assert.Null(bobView.NoteTitle);
        Assert.Equal("Quarterly secrets", (await (await As(1).GetAsync($"/api/shoutouts/{created.ShoutOutId}")).ReadAsync<ShoutOut>()).NoteTitle);
    }

    [Fact]
    public async Task Senders_AreLimitedToTenShoutOutsPerHour()
    {
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Created, (await Send(As(3), 1, new { kind = "Info", message = $"m{i}" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(As(3), 1, new { kind = "Info", message = "one too many" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(As(4), 1, new { kind = "Info", message = "different sender" })).StatusCode);
    }

    [Fact]
    public async Task Notification_LinksBackToTheShoutOut()
    {
        var created = await (await Send(As(3), 1, new { kind = "Review", message = "link test" })).ReadAsync<ShoutOut>(HttpStatusCode.Created);
        var raw = await (await As(2).GetAsync("/api/notifications")).Content.ReadAsStringAsync();
        Assert.Contains($"\"shoutOutId\":{created.ShoutOutId}", raw);
    }
}
