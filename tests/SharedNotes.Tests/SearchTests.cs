using System.Net;

namespace SharedNotes.Tests;

public class SearchTests : IDisposable
{
    private readonly AppFactory _factory = new();
    private readonly HttpClient _alice;

    public SearchTests() => _alice = _factory.ClientFor(1);

    public void Dispose() => _factory.Dispose();

    private async Task<List<Note>> SearchAsync(HttpClient client, string term) =>
        await (await client.GetAsync($"/api/notes?search={Uri.EscapeDataString(term)}")).ReadAsync<List<Note>>();

    [Fact]
    public async Task SeedNotes_AreIndexed()
    {
        Assert.Equal([2], (await SearchAsync(_alice, "sprint")).Select(n => n.NoteId));
    }

    [Fact]
    public async Task Search_Stems_Words()
    {
        await _alice.CreateNoteAsync("Planning the launch", content: "details");
        // "plans" and "planning" share the stem "plan".
        var results = await SearchAsync(_alice, "plans");
        Assert.Contains(results, n => n.Title == "Planning the launch");
    }

    [Fact]
    public async Task Search_MatchesPrefixes_AndIgnoresCaseAndAccents()
    {
        await _alice.CreateNoteAsync("Café menu", content: "Seasonal SPECIALS");
        Assert.Contains(await SearchAsync(_alice, "cafe"), n => n.Title == "Café menu");
        Assert.Contains(await SearchAsync(_alice, "speci"), n => n.Title == "Café menu");
    }

    [Fact]
    public async Task Search_RequiresAllWords()
    {
        await _alice.CreateNoteAsync("Alpha bravo");
        await _alice.CreateNoteAsync("Alpha charlie");
        var results = await SearchAsync(_alice, "alpha bravo");
        Assert.Equal(["Alpha bravo"], results.Select(n => n.Title));
    }

    [Fact]
    public async Task Search_RanksTitleMatchesAboveContentMatches()
    {
        await _alice.CreateNoteAsync("Unrelated heading", content: "we discuss zebra migration here");
        await _alice.CreateNoteAsync("Zebra migration", content: "short");
        var results = await SearchAsync(_alice, "zebra");
        Assert.Equal(["Zebra migration", "Unrelated heading"], results.Select(n => n.Title));
    }

    [Fact]
    public async Task Search_ReflectsEditsAndDeletes()
    {
        var note = await _alice.CreateNoteAsync("Original title", content: "text");
        Assert.Single(await SearchAsync(_alice, "original"));

        var updated = await (await _alice.PutJsonAsync($"/api/notes/{note.NoteId}",
            HttpExtensions.UpdateBody(note, note.Version, title: "Renamed"))).ReadAsync<Note>();
        Assert.Empty(await SearchAsync(_alice, "original"));
        Assert.Single(await SearchAsync(_alice, "renamed"));

        await _alice.DeleteAsync($"/api/notes/{updated.NoteId}");
        Assert.Empty(await SearchAsync(_alice, "renamed"));
    }

    [Fact]
    public async Task Search_NeverReturnsNotesTheEmployeeCannotRead()
    {
        // Seed note 1 ("My private to-do list") is Alice's private note; note 2 is Engineering only.
        Assert.Single(await SearchAsync(_alice, "private"));
        Assert.Empty(await SearchAsync(_factory.ClientFor(2), "private"));
        Assert.Empty(await SearchAsync(_factory.ClientFor(3), "sprint"));
    }

    [Fact]
    public async Task Search_CombinesWithOtherFilters()
    {
        var response = await _alice.GetAsync("/api/notes?search=planning&visibility=Team");
        Assert.Equal([2], (await response.ReadAsync<List<Note>>()).Select(n => n.NoteId));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("*")]
    [InlineData("title:foo OR bar")]
    [InlineData("NEAR(a b)")]
    [InlineData("(unbalanced")]
    [InlineData("!!!")]
    public async Task Search_WithSyntaxCharacters_NeverErrors(string term)
    {
        var response = await _alice.GetAsync($"/api/notes?search={Uri.EscapeDataString(term)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
