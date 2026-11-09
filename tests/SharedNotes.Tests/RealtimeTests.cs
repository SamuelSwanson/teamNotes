using System.Net;
using System.Text.Json;

namespace SharedNotes.Tests;

public record SseEvent(string Type, JsonElement Data);

/// <summary>Minimal Server-Sent Events client for tests.</summary>
public sealed class SseReader : IDisposable
{
    private readonly HttpResponseMessage _response;
    private readonly StreamReader _reader;
    private Task<string?>? _pending;

    private SseReader(HttpResponseMessage response, StreamReader reader) => (_response, _reader) = (response, reader);

    public static async Task<SseReader> ConnectAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return new SseReader(response, new StreamReader(await response.Content.ReadAsStreamAsync()));
    }

    private async Task<string?> ReadLineAsync(TimeSpan timeout)
    {
        _pending ??= _reader.ReadLineAsync();
        if (await Task.WhenAny(_pending, Task.Delay(timeout)) != _pending) return null;   // timed out; keep the pending read
        var line = await _pending;
        _pending = null;
        return line;
    }

    /// <summary>Returns the next event (skipping comments) or null if none arrives within the timeout.</summary>
    public async Task<SseEvent?> NextAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        string? type = null;
        while (DateTime.UtcNow < deadline)
        {
            var line = await ReadLineAsync(deadline - DateTime.UtcNow);
            if (line is null) return null;
            if (line.StartsWith("event: ")) type = line[7..];
            else if (line.StartsWith("data: ") && type is not null)
                return new SseEvent(type, JsonDocument.Parse(line[6..]).RootElement.Clone());
        }
        return null;
    }

    public void Dispose()
    {
        _response.Dispose();
    }
}

public class RealtimeTests : IDisposable
{
    private readonly AppFactory _factory;

    public RealtimeTests() => _factory = new AppFactory(new() { ["Presence:TtlSeconds"] = "1", ["Presence:SweepSeconds"] = "1" });

    public void Dispose() => _factory.Dispose();

    private static string Describe(SseEvent? e) => e is null ? "none" : $"{e.Type} {e.Data}";

    [Fact]
    public async Task TeamMember_ReceivesChangeEvent_WithoutNoteContent()
    {
        using var bobStream = await SseReader.ConnectAsync(_factory.ClientFor(2));   // Engineering member
        var alice = _factory.ClientFor(1);
        var note = await (await alice.GetAsync("/api/notes/2")).ReadAsync<Note>();   // Engineering team note
        var updated = await (await alice.PutJsonAsync("/api/notes/2",
            HttpExtensions.UpdateBody(note, note.Version, title: "SECRET TITLE"))).ReadAsync<Note>();

        var ev = await bobStream.NextAsync();
        Assert.NotNull(ev);
        Assert.Equal("note-changed", ev.Type);
        Assert.Equal(2, ev.Data.GetProperty("noteId").GetInt32());
        Assert.Equal(1, ev.Data.GetProperty("actorId").GetInt32());
        Assert.Equal(updated.Version, ev.Data.GetProperty("version").GetGuid());
        Assert.DoesNotContain("SECRET TITLE", ev.Data.ToString());
    }

    [Fact]
    public async Task Events_AreOnlyDeliveredToEmployeesWhoCanReadTheNote()
    {
        using var carolStream = await SseReader.ConnectAsync(_factory.ClientFor(3));   // Marketing: not in Engineering
        var alice = _factory.ClientFor(1);

        await alice.CreateNoteAsync("private one", visibility: "Private");
        var engineering = await alice.CreateNoteAsync("eng only", visibility: "Team", teamId: 1);
        var everyone = await alice.CreateNoteAsync("for all", visibility: "All");

        // Carol must see the public note first; the private and Engineering events were filtered out.
        var ev = await carolStream.NextAsync();
        Assert.NotNull(ev);
        Assert.Equal(everyone.NoteId, ev.Data.GetProperty("noteId").GetInt32());
        Assert.NotEqual(engineering.NoteId, ev.Data.GetProperty("noteId").GetInt32());
        Assert.Null(await carolStream.NextAsync(TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task Delete_PublishesDeletedEvent()
    {
        using var daveStream = await SseReader.ConnectAsync(_factory.ClientFor(4));
        var alice = _factory.ClientFor(1);
        var note = await alice.CreateNoteAsync();
        Assert.Equal("note-changed", (await daveStream.NextAsync())?.Type);

        await alice.DeleteAsync($"/api/notes/{note.NoteId}");
        var ev = await daveStream.NextAsync();
        Assert.Equal("note-deleted", ev?.Type);
        Assert.Equal(note.NoteId, ev!.Data.GetProperty("noteId").GetInt32());
    }

    [Fact]
    public async Task EventStream_RequiresSignIn()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/events")).StatusCode);
    }

    [Fact]
    public async Task Presence_ShowsOtherEditors_AndStopsOnRequest()
    {
        var alice = _factory.ClientFor(1);
        var bob = _factory.ClientFor(2);
        using var aliceStream = await SseReader.ConnectAsync(alice);

        Assert.Equal(HttpStatusCode.NoContent, (await bob.PutAsync("/api/notes/2/editing", null)).StatusCode);

        var started = await aliceStream.NextAsync();
        Assert.Equal("editing", started?.Type);
        Assert.True(started!.Data.GetProperty("active").GetBoolean());
        Assert.Equal("Bob Martinez", started.Data.GetProperty("actorName").GetString());

        var editors = await (await alice.GetAsync("/api/notes/2/editors")).ReadAsync<List<JsonElement>>();
        Assert.Equal([2], editors.Select(e => e.GetProperty("employeeId").GetInt32()));
        Assert.Empty(await (await bob.GetAsync("/api/notes/2/editors")).ReadAsync<List<JsonElement>>());   // excludes yourself

        await bob.DeleteAsync("/api/notes/2/editing");
        var stopped = await aliceStream.NextAsync();
        Assert.False(stopped!.Data.GetProperty("active").GetBoolean());
        Assert.Empty(await (await alice.GetAsync("/api/notes/2/editors")).ReadAsync<List<JsonElement>>());
    }

    [Fact]
    public async Task Presence_ExpiresWithoutHeartbeat_AndHeartbeatKeepsItAlive()
    {
        var alice = _factory.ClientFor(1);
        var bob = _factory.ClientFor(2);
        using var aliceStream = await SseReader.ConnectAsync(alice);

        await bob.PutAsync("/api/notes/2/editing", null);
        Assert.Equal("editing", (await aliceStream.NextAsync())?.Type);

        // Heartbeats within the 1s TTL do not announce a second "started" and keep Bob listed.
        await Task.Delay(600);
        await bob.PutAsync("/api/notes/2/editing", null);
        await Task.Delay(600);
        Assert.Single(await (await alice.GetAsync("/api/notes/2/editors")).ReadAsync<List<JsonElement>>());

        // No more heartbeats: the sweeper announces that Bob stopped.
        var expired = await aliceStream.NextAsync(TimeSpan.FromSeconds(6));
        Assert.True(expired is not null, "expected a stop event, got " + Describe(expired));
        Assert.False(expired!.Data.GetProperty("active").GetBoolean());
        Assert.Empty(await (await alice.GetAsync("/api/notes/2/editors")).ReadAsync<List<JsonElement>>());
    }

    [Fact]
    public async Task Presence_FollowsNoteAccessRules()
    {
        var bob = _factory.ClientFor(2);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsync("/api/notes/1/editing", null)).StatusCode);   // Alice's private note
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/notes/1/editors")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsync("/api/notes/9999/editing", null)).StatusCode);
    }
}
