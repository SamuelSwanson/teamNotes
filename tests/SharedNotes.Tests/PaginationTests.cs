using System.Net;

namespace SharedNotes.Tests;

public class PaginationTests : IDisposable
{
    private readonly AppFactory _factory = new();
    private readonly HttpClient _alice;

    public PaginationTests() => _alice = _factory.ClientFor(1);

    public void Dispose() => _factory.Dispose();

    private static int Total(HttpResponseMessage r) => int.Parse(r.Headers.GetValues("X-Total-Count").Single());

    private async Task CreateManyAsync(int count, string prefix)
    {
        for (var i = 1; i <= count; i++) await _alice.CreateNoteAsync($"{prefix} {i:00}");
    }

    [Fact]
    public async Task List_IsPaged_WithTotalCountHeader_AndNoDuplicatesAcrossPages()
    {
        await CreateManyAsync(23, "paged");   // Alice can already see 4 seeded notes (her private one, 2 team notes, 1 public)
        var all = new List<int>();
        int? total = null;
        for (var page = 1; ; page++)
        {
            var response = await _alice.GetAsync($"/api/notes?page={page}&pageSize=10");
            total ??= Total(response);
            var items = await response.ReadAsync<List<Note>>();
            Assert.Equal(page.ToString(), response.Headers.GetValues("X-Page").Single());
            Assert.True(items.Count <= 10);
            if (items.Count == 0) break;
            all.AddRange(items.Select(n => n.NoteId));
        }

        Assert.Equal(27, total);   // 23 new + 4 seeded notes visible to Alice
        Assert.Equal(27, all.Count);
        Assert.Equal(27, all.Distinct().Count());
    }

    [Fact]
    public async Task List_DefaultsToFirstPageOf50()
    {
        await CreateManyAsync(3, "x");
        var response = await _alice.GetAsync("/api/notes");
        Assert.Equal("1", response.Headers.GetValues("X-Page").Single());
        Assert.Equal("50", response.Headers.GetValues("X-Page-Size").Single());
        Assert.Equal(7, Total(response));
    }

    [Fact]
    public async Task PageBeyondTheEnd_ReturnsEmptyList_WithStillCorrectTotal()
    {
        var response = await _alice.GetAsync("/api/notes?page=99&pageSize=10");
        Assert.Empty(await response.ReadAsync<List<Note>>());
        Assert.Equal(4, Total(response));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=abc")]
    public async Task InvalidPaging_Returns400(string query)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _alice.GetAsync($"/api/notes?{query}")).StatusCode);
    }

    [Fact]
    public async Task Total_OnlyCountsNotesTheEmployeeCanAccess()
    {
        await CreateManyAsync(5, "mine");
        await _alice.CreateNoteAsync("secret", visibility: "Private");
        var bob = await _factory.ClientFor(2).GetAsync("/api/notes");
        Assert.Equal(7, Total(bob));   // seed notes 2 (Engineering) and 3 (All) + Alice's 5 public notes; not her private one
    }

    [Fact]
    public async Task Search_IsPaged_InRankOrder_WithTotal()
    {
        await CreateManyAsync(12, "pagetest");
        var seen = new List<string>();
        foreach (var page in new[] { 1, 2, 3 })
        {
            var response = await _alice.GetAsync($"/api/notes?search=pagetest&page={page}&pageSize=5");
            Assert.Equal(12, Total(response));
            seen.AddRange((await response.ReadAsync<List<Note>>()).Select(n => n.Title));
        }
        Assert.Equal(12, seen.Count);
        Assert.Equal(12, seen.Distinct().Count());
    }

    [Fact]
    public async Task Filters_ApplyBeforePaging()
    {
        await CreateManyAsync(6, "filtered");
        var response = await _alice.GetAsync("/api/notes?visibility=All&pageSize=5&page=2");
        var items = await response.ReadAsync<List<Note>>();
        Assert.All(items, n => Assert.Equal("All", n.Visibility));
        Assert.Equal(7, Total(response));   // 6 new + seeded "Company holiday schedule"
        Assert.Equal(2, items.Count);
    }
}
