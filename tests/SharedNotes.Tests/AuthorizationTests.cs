using System.Net;

namespace SharedNotes.Tests;

// Seed: note 1 = Alice's Private, note 2 = Bob's Team(Engineering), note 3 = Carol's All, note 4 = Carol's Team(Marketing).
// Alice: Engineering+Marketing. Bob: Engineering. Carol: Marketing. Dave: no teams.
public class AuthorizationTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<HttpStatusCode> GetStatus(int employeeId, int noteId) =>
        (await _factory.ClientFor(employeeId).GetAsync($"/api/notes/{noteId}")).StatusCode;

    [Fact]
    public async Task MissingIdentity_Returns401()
    {
        var response = await _factory.ClientFor(null).GetAsync("/api/notes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("abc")]
    [InlineData("0")]
    public async Task InvalidIdentity_Returns401(string header)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Employee-Id", header);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notes")).StatusCode);
    }

    [Fact]
    public async Task PrivateNote_OnlyCreatorCanAccess()
    {
        Assert.Equal(HttpStatusCode.OK, await GetStatus(1, 1));
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatus(2, 1));
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatus(4, 1));

        var bobList = await (await _factory.ClientFor(2).GetAsync("/api/notes")).ReadAsync<List<Note>>();
        Assert.DoesNotContain(bobList, n => n.NoteId == 1);
    }

    [Fact]
    public async Task TeamNote_OnlyTeamMembersCanAccess()
    {
        Assert.Equal(HttpStatusCode.OK, await GetStatus(2, 2));         // Bob: Engineering member
        Assert.Equal(HttpStatusCode.OK, await GetStatus(1, 2));         // Alice: Engineering member
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatus(3, 2));  // Carol: Marketing only
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatus(4, 2));  // Dave: no teams

        var carolList = await (await _factory.ClientFor(3).GetAsync("/api/notes")).ReadAsync<List<Note>>();
        Assert.DoesNotContain(carolList, n => n.NoteId == 2);
        Assert.Contains(carolList, n => n.NoteId == 4);
    }

    [Fact]
    public async Task AllNote_AccessibleToEveryone_AndEditableByAnyone()
    {
        foreach (var id in new[] { 1, 2, 3, 4 })
            Assert.Equal(HttpStatusCode.OK, await GetStatus(id, 3));

        var dave = _factory.ClientFor(4);
        var note = await (await dave.GetAsync("/api/notes/3")).ReadAsync<Note>();
        var updated = await (await dave.PutJsonAsync("/api/notes/3",
            HttpExtensions.UpdateBody(note, note.Version, title: "Edited by Dave"))).ReadAsync<Note>();
        Assert.Equal(4, updated.UpdatedByEmployeeId);
    }

    [Fact]
    public async Task TeamMembers_CanEditTeamNote_AndNonMembersCannot()
    {
        var alice = _factory.ClientFor(1);   // member, not the creator (Bob is)
        var note = await (await alice.GetAsync("/api/notes/2")).ReadAsync<Note>();

        var updated = await (await alice.PutJsonAsync("/api/notes/2",
            HttpExtensions.UpdateBody(note, note.Version, content: "Edited by Alice"))).ReadAsync<Note>();
        Assert.Equal("Edited by Alice", updated.Content);
        Assert.Equal(1, updated.UpdatedByEmployeeId);
        Assert.Equal(2, updated.CreatedByEmployeeId);

        var carol = _factory.ClientFor(3);
        var denied = await carol.PutJsonAsync("/api/notes/2", HttpExtensions.UpdateBody(updated, updated.Version, content: "Hacked"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task OnlyCreator_CanDelete()
    {
        // Alice can read and edit Bob's team note but must not delete it.
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.ClientFor(1).DeleteAsync("/api/notes/2")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.ClientFor(4).DeleteAsync("/api/notes/3")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _factory.ClientFor(2).DeleteAsync("/api/notes/2")).StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownNote_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.ClientFor(1).DeleteAsync("/api/notes/9999")).StatusCode);
    }

    [Theory]
    [InlineData(2, 2)]   // Bob is not in Marketing
    [InlineData(4, 1)]   // Dave is in no team
    public async Task CreateTeamNote_ForTeamYouDoNotBelongTo_Returns403(int employeeId, int teamId)
    {
        var response = await _factory.ClientFor(employeeId).PostJsonAsync("/api/notes",
            new { title = "t", content = "c", visibility = "Team", teamId });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateTeamNote_ForOwnTeam_Succeeds_AndMultiTeamMemberCanUseEither()
    {
        var alice = _factory.ClientFor(1);
        Assert.Equal(1, (await alice.CreateNoteAsync(visibility: "Team", teamId: 1)).TeamId);
        Assert.Equal(2, (await alice.CreateNoteAsync(visibility: "Team", teamId: 2)).TeamId);
    }

    [Fact]
    public async Task TeamIdRules_AreValidated()
    {
        var alice = _factory.ClientFor(1);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostJsonAsync("/api/notes",
            new { title = "t", content = "c", visibility = "Team" })).StatusCode);                    // Team without TeamId
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostJsonAsync("/api/notes",
            new { title = "t", content = "c", visibility = "Private", teamId = 1 })).StatusCode);     // Private with TeamId
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostJsonAsync("/api/notes",
            new { title = "t", content = "c", visibility = "Team", teamId = 999 })).StatusCode);      // Unknown team
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostJsonAsync("/api/notes",
            new { title = "t", content = "c", visibility = "Bogus" })).StatusCode);                   // Unknown visibility
    }

    [Fact]
    public async Task OnlyCreator_CanChangeVisibility()
    {
        var alice = _factory.ClientFor(1);
        var note = await (await alice.GetAsync("/api/notes/2")).ReadAsync<Note>();   // Bob's team note
        var response = await alice.PutJsonAsync("/api/notes/2", new
        {
            title = note.Title, content = note.Content, visibility = "All", expectedVersion = note.Version,
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
