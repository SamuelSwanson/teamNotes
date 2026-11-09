using System.Net;

namespace SharedNotes.Tests;

public record Revision(int RevisionId, string Action, string Title, string Content, string Visibility, string? TeamName,
    string Topics, Guid NoteVersion, int EditedByEmployeeId, string EditedByName, DateTime EditedAt);

public class HistoryTests : IDisposable
{
    private readonly AppFactory _factory = new();
    private readonly HttpClient _alice;

    public HistoryTests() => _alice = _factory.ClientFor(1);

    public void Dispose() => _factory.Dispose();

    private static async Task<List<Revision>> HistoryAsync(HttpClient c, int noteId) =>
        await (await c.GetAsync($"/api/notes/{noteId}/history")).ReadAsync<List<Revision>>();

    [Fact]
    public async Task CreatingANote_RecordsAFirstRevision()
    {
        var note = await _alice.CreateNoteAsync("First draft", topicIds: [2, 1]);
        var history = await HistoryAsync(_alice, note.NoteId);

        var created = Assert.Single(history);
        Assert.Equal("Created", created.Action);
        Assert.Equal("First draft", created.Title);
        Assert.Equal("Meeting Notes, Planning", created.Topics);
        Assert.Equal(note.Version, created.NoteVersion);
        Assert.Equal(1, created.EditedByEmployeeId);
    }

    [Fact]
    public async Task EachUpdate_AddsARevision_NewestFirst_WithEditorAndOldContentPreserved()
    {
        var note = await _alice.CreateNoteAsync("v1", content: "original text");
        var bob = _factory.ClientFor(2);
        var v2 = await (await bob.PutJsonAsync($"/api/notes/{note.NoteId}",
            HttpExtensions.UpdateBody(note, note.Version, title: "v2", content: "edited by bob"))).ReadAsync<Note>();
        await _alice.PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(v2, v2.Version, title: "v3"));

        var history = await HistoryAsync(_alice, note.NoteId);
        Assert.Equal(["v3", "v2", "v1"], history.Select(r => r.Title));
        Assert.Equal(["Updated", "Updated", "Created"], history.Select(r => r.Action));
        Assert.Equal([1, 2, 1], history.Select(r => r.EditedByEmployeeId));
        Assert.Equal("original text", history[2].Content);
        Assert.Equal("edited by bob", history[1].Content);
        Assert.Equal(v2.Version, history[1].NoteVersion);
    }

    [Fact]
    public async Task RejectedConflictingUpdate_LeavesNoRevision()
    {
        var note = await _alice.CreateNoteAsync("original");
        await _alice.PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(note, note.Version, title: "winner"));
        var stale = await _alice.PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(note, note.Version, title: "loser"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var history = await HistoryAsync(_alice, note.NoteId);
        Assert.Equal(["winner", "original"], history.Select(r => r.Title));
    }

    [Fact]
    public async Task History_FollowsNoteAccessRules()
    {
        // Seed note 1 is Alice's private note.
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.ClientFor(2).GetAsync("/api/notes/1/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _alice.GetAsync("/api/notes/9999/history")).StatusCode);
        Assert.Single(await HistoryAsync(_alice, 1));   // seeded notes start with a Created revision
    }

    [Fact]
    public async Task TeamName_IsRecorded_AndHistoryIsRemovedWithTheNote()
    {
        var note = await _alice.CreateNoteAsync(visibility: "Team", teamId: 2);
        Assert.Equal("Marketing", (await HistoryAsync(_alice, note.NoteId)).Single().TeamName);

        await _alice.DeleteAsync($"/api/notes/{note.NoteId}");
        Assert.Equal(HttpStatusCode.NotFound, (await _alice.GetAsync($"/api/notes/{note.NoteId}/history")).StatusCode);
    }
}
