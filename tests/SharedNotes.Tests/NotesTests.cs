using System.Net;

namespace SharedNotes.Tests;

public class NotesTests : IDisposable
{
    private readonly AppFactory _factory = new();
    private readonly HttpClient _alice;

    public NotesTests() => _alice = _factory.ClientFor(1);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Create_ReturnsNote_WithIdentityTakenFromHeaderNotBody()
    {
        // The client tries to spoof ownership; the server must ignore these fields.
        var response = await _alice.PostJsonAsync("/api/notes", new
        {
            title = "Hello", content = "World", visibility = "All",
            createdByEmployeeId = 3, updatedByEmployeeId = 3,
        });
        var note = await response.ReadAsync<Note>(HttpStatusCode.Created);

        Assert.Equal(1, note.CreatedByEmployeeId);
        Assert.Equal(1, note.UpdatedByEmployeeId);
        Assert.Equal("All", note.Visibility);
        Assert.NotEqual(Guid.Empty, note.Version);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task Get_ReturnsNote()
    {
        var created = await _alice.CreateNoteAsync("Findable");
        var fetched = await (await _alice.GetAsync($"/api/notes/{created.NoteId}")).ReadAsync<Note>();
        Assert.Equal("Findable", fetched.Title);
        Assert.Equal(created.Version, fetched.Version);
    }

    [Fact]
    public async Task Get_UnknownNote_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _alice.GetAsync("/api/notes/9999")).StatusCode);
    }

    [Fact]
    public async Task Update_ChangesFields_AndAuditInfo()
    {
        var created = await _alice.CreateNoteAsync("Old title");
        var bob = _factory.ClientFor(2);   // "All" notes are editable by everyone

        var updated = await (await bob.PutJsonAsync($"/api/notes/{created.NoteId}",
            HttpExtensions.UpdateBody(created, created.Version, title: "New title", content: "New content")))
            .ReadAsync<Note>();

        Assert.Equal("New title", updated.Title);
        Assert.Equal("New content", updated.Content);
        Assert.Equal(1, updated.CreatedByEmployeeId);
        Assert.Equal(2, updated.UpdatedByEmployeeId);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);
    }

    [Fact]
    public async Task Delete_ByCreator_RemovesNote()
    {
        var created = await _alice.CreateNoteAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await _alice.DeleteAsync($"/api/notes/{created.NoteId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _alice.GetAsync($"/api/notes/{created.NoteId}")).StatusCode);
    }

    [Fact]
    public async Task Update_WithMissingExpectedVersion_Returns400()
    {
        var created = await _alice.CreateNoteAsync();
        var response = await _alice.PutJsonAsync($"/api/notes/{created.NoteId}",
            new { title = "x", content = "y", visibility = "All" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInvalidBody_Returns400ProblemDetails()
    {
        var response = await _alice.PostJsonAsync("/api/notes", new { title = "", content = "c", visibility = "All" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Search_MatchesTitleAndContent_CaseInsensitive()
    {
        await _alice.CreateNoteAsync("Quarterly Budget", content: "numbers");
        await _alice.CreateNoteAsync("Other", content: "mentions the BUDGET here");
        await _alice.CreateNoteAsync("Unrelated", content: "nothing");

        var results = await (await _alice.GetAsync("/api/notes?search=budget")).ReadAsync<List<Note>>();
        Assert.Equal(2, results.Count);
        Assert.All(results, n => Assert.Contains("budget", (n.Title + n.Content).ToLower()));
    }

    [Fact]
    public async Task Filter_ByTeam_ReturnsOnlyThatTeamsNotes()
    {
        var results = await (await _alice.GetAsync("/api/notes?teamId=1")).ReadAsync<List<Note>>();
        Assert.Equal([2], results.Select(n => n.NoteId));   // seed note 2 belongs to Engineering
    }

    [Fact]
    public async Task Filter_ByTopic_ReturnsOnlyNotesWithThatTopic()
    {
        var results = await (await _alice.GetAsync("/api/notes?topicId=1")).ReadAsync<List<Note>>();
        Assert.Equal([1, 2], results.Select(n => n.NoteId).Order());   // seed: notes 1 and 2 carry "Planning"
    }

    [Fact]
    public async Task Filter_ByVisibility_AndCreator()
    {
        var all = await (await _alice.GetAsync("/api/notes?visibility=All")).ReadAsync<List<Note>>();
        Assert.All(all, n => Assert.Equal("All", n.Visibility));

        var byCarol = await (await _alice.GetAsync("/api/notes?createdBy=3")).ReadAsync<List<Note>>();
        Assert.NotEmpty(byCarol);
        Assert.All(byCarol, n => Assert.Equal(3, n.CreatedByEmployeeId));
    }

    [Fact]
    public async Task Create_WithMultipleTopics_AssignsAll_AndUpdateReplacesThem()
    {
        var note = await _alice.CreateNoteAsync(topicIds: [1, 2, 3]);
        Assert.Equal([1, 2, 3], note.Topics.Select(t => t.TopicId).Order());

        var updated = await (await _alice.PutJsonAsync($"/api/notes/{note.NoteId}",
            HttpExtensions.UpdateBody(note, note.Version, topicIds: [3]))).ReadAsync<Note>();
        Assert.Equal([3], updated.Topics.Select(t => t.TopicId));
    }
}
