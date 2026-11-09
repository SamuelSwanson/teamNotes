using System.Net;

namespace SharedNotes.Tests;

public class ConcurrencyTests : IDisposable
{
    private readonly AppFactory _factory = new();
    private readonly HttpClient _alice;

    public ConcurrencyTests() => _alice = _factory.ClientFor(1);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task SuccessfulUpdate_ChangesVersion()
    {
        var note = await _alice.CreateNoteAsync();
        var updated = await (await _alice.PutJsonAsync($"/api/notes/{note.NoteId}",
            HttpExtensions.UpdateBody(note, note.Version, title: "v2"))).ReadAsync<Note>();

        Assert.NotEqual(note.Version, updated.Version);
        var fetched = await (await _alice.GetAsync($"/api/notes/{note.NoteId}")).ReadAsync<Note>();
        Assert.Equal(updated.Version, fetched.Version);
    }

    [Fact]
    public async Task OutdatedVersion_Returns409_AndDoesNotOverwrite()
    {
        var note = await _alice.CreateNoteAsync("original");
        var bob = _factory.ClientFor(2);

        var winner = await (await bob.PutJsonAsync($"/api/notes/{note.NoteId}",
            HttpExtensions.UpdateBody(note, note.Version, title: "bob wins"))).ReadAsync<Note>();

        // Alice still holds the original version.
        var stale = await _alice.PutJsonAsync($"/api/notes/{note.NoteId}",
            HttpExtensions.UpdateBody(note, note.Version, title: "alice loses"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("application/problem+json", stale.Content.Headers.ContentType?.MediaType);

        var current = await (await _alice.GetAsync($"/api/notes/{note.NoteId}")).ReadAsync<Note>();
        Assert.Equal("bob wins", current.Title);
        Assert.Equal(winner.Version, current.Version);
        Assert.Equal(2, current.UpdatedByEmployeeId);
    }

    [Fact]
    public async Task StaleVersion_DoesNotChangeTopics()
    {
        var note = await _alice.CreateNoteAsync(topicIds: [1]);
        await _alice.PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(note, note.Version, title: "first"));

        var stale = await _alice.PutJsonAsync($"/api/notes/{note.NoteId}",
            HttpExtensions.UpdateBody(note, note.Version, topicIds: [2, 3]));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var current = await (await _alice.GetAsync($"/api/notes/{note.NoteId}")).ReadAsync<Note>();
        Assert.Equal([1], current.Topics.Select(t => t.TopicId));   // rolled back with the rejected update
    }

    [Fact]
    public async Task SimultaneousUpdates_WithSameVersion_OnlyOneSucceeds()
    {
        const int rounds = 5, racers = 4;
        for (var round = 0; round < rounds; round++)
        {
            var note = await _alice.CreateNoteAsync($"round {round}");
            var clients = new[] { 1, 2, 3, 4 }.Select(id => _factory.ClientFor(id)).ToArray();

            // Everyone starts from the same version and fires at (nearly) the same moment.
            using var gate = new ManualResetEventSlim(false);
            var tasks = Enumerable.Range(0, racers).Select(i => Task.Run(async () =>
            {
                gate.Wait();
                return await clients[i].PutJsonAsync($"/api/notes/{note.NoteId}",
                    HttpExtensions.UpdateBody(note, note.Version, title: $"racer {i}"));
            })).ToArray();
            gate.Set();
            var responses = await Task.WhenAll(tasks);

            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.OK));
            Assert.Equal(racers - 1, statuses.Count(s => s == HttpStatusCode.Conflict));

            // The stored note is exactly the winner's; losers did not overwrite it.
            var winnerIndex = statuses.IndexOf(HttpStatusCode.OK);
            var winner = await responses[winnerIndex].ReadAsync<Note>();
            var current = await (await _alice.GetAsync($"/api/notes/{note.NoteId}")).ReadAsync<Note>();
            Assert.Equal($"racer {winnerIndex}", current.Title);
            Assert.Equal(winner.Version, current.Version);
            Assert.Equal(winnerIndex + 1, current.UpdatedByEmployeeId);
        }
    }
}
