using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace SharedNotes.Tests;

/// <summary>
/// Hosts the real app against an isolated SQLite in-memory database (shared-cache, so every request's
/// DbContext gets its own connection to the same database). A keeper connection stays open for the factory's lifetime.
/// Migrations run on startup, so the seed data is present: employees 1-4, teams 1-2, topics 1-3, notes 1-4.
/// Seed memberships: Alice(1) in Engineering(1)+Marketing(2), Bob(2) in Engineering, Carol(3) in Marketing, Dave(4) in none.
/// </summary>
public class AppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _keeper;
    private readonly string _connectionString = $"Data Source=file:test_{Guid.NewGuid():N}?mode=memory&cache=shared";

    private readonly Dictionary<string, string> _settings;

    public AppFactory(Dictionary<string, string>? settings = null)
    {
        _settings = settings ?? [];
        _keeper = new SqliteConnection(_connectionString);
        _keeper.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        // Test-only: act as any employee via X-Employee-Id without a password round-trip (disabled in the real app).
        builder.UseSetting("Auth:AllowHeaderIdentity", "true");
        foreach (var (key, value) in _settings) builder.UseSetting(key, value);
        builder.UseEnvironment("Testing");
    }

    public HttpClient ClientFor(int? employeeId)
    {
        var client = CreateClient();
        if (employeeId is not null) client.DefaultRequestHeaders.Add("X-Employee-Id", employeeId.ToString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keeper.Dispose();
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

public record Topic(int TopicId, string Name, string? Description);

public record Note(int NoteId, string Title, string Content, string Visibility, int? TeamId, List<Topic> Topics,
    int CreatedByEmployeeId, int UpdatedByEmployeeId, DateTime CreatedAt, DateTime UpdatedAt, Guid Version, bool CanDelete);

public static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient c, string url, object body) =>
        c.PostAsJsonAsync(url, body, Json.Options);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient c, string url, object body) =>
        c.PutAsJsonAsync(url, body, Json.Options);

    /// <summary>Creates a note and returns it. Fails the test if creation is not 201.</summary>
    public static async Task<Note> CreateNoteAsync(this HttpClient c, string title = "Test note", string visibility = "All",
        int? teamId = null, int[]? topicIds = null, string content = "Some content") =>
        await (await c.PostJsonAsync("/api/notes", new { title, content, visibility, teamId, topicIds }))
            .ReadAsync<Note>(HttpStatusCode.Created);

    public static object UpdateBody(Note n, Guid expected, string? title = null, string? content = null, int[]? topicIds = null) => new
    {
        title = title ?? n.Title,
        content = content ?? n.Content,
        visibility = n.Visibility,
        teamId = n.TeamId,
        topicIds = topicIds ?? n.Topics.Select(t => t.TopicId).ToArray(),
        expectedVersion = expected,
    };
}
