using System.Collections.Concurrent;
using System.Threading.Channels;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Services;

/// <summary>
/// An event about a note. <see cref="Visibility"/>, <see cref="TeamId"/> and <see cref="OwnerId"/> describe who may
/// see it (the same rules as reading the note); they are used for filtering and are never sent to clients.
/// </summary>
public record NoteEvent(string Type, int NoteId, Visibility Visibility, int? TeamId, int OwnerId,
    int ActorId, string ActorName, Guid? Version = null, bool? Active = null)
{
    public const string Changed = "note-changed";
    public const string Deleted = "note-deleted";
    public const string Editing = "editing";
    public const string Notification = "notification";
}

/// <summary>
/// In-process fan-out of note events to connected browsers (Server-Sent Events). Each connection owns a small bounded
/// channel; slow clients lose their oldest events rather than blocking writers. With more than one server instance
/// this would need a shared backplane (e.g. Redis pub/sub); in this single-process monolith memory is enough.
/// </summary>
public class NoteEventBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<NoteEvent>> _subscribers = new();

    public Subscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<NoteEvent>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest });
        _subscribers[id] = channel;
        return new Subscription(channel.Reader, () => _subscribers.TryRemove(id, out _));
    }

    public void Publish(NoteEvent note)
    {
        foreach (var channel in _subscribers.Values) channel.Writer.TryWrite(note);
    }

    public sealed class Subscription(ChannelReader<NoteEvent> reader, Action onDispose) : IDisposable
    {
        public ChannelReader<NoteEvent> Reader { get; } = reader;
        public void Dispose() => onDispose();
    }
}

/// <summary>Tracks who currently has a note open in the editor. Entries expire unless refreshed by heartbeats.</summary>
public class PresenceTracker(TimeProvider time, IConfiguration config)
{
    private record Entry(DateTimeOffset LastSeen, string Name, NoteEvent Template);

    private readonly ConcurrentDictionary<(int NoteId, int EmployeeId), Entry> _entries = new();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(config.GetValue("Presence:TtlSeconds", 45));

    /// <summary>Records a heartbeat. Returns true when the employee was not already marked as editing.</summary>
    public bool Touch(int noteId, int employeeId, string name, NoteEvent template)
    {
        var entry = new Entry(time.GetUtcNow(), name, template);
        var isNew = true;
        _entries.AddOrUpdate((noteId, employeeId), entry, (_, old) =>
        {
            isNew = old.LastSeen + _ttl <= entry.LastSeen;
            return entry;
        });
        return isNew;
    }

    public bool Remove(int noteId, int employeeId) => _entries.TryRemove((noteId, employeeId), out _);

    public List<(int EmployeeId, string Name)> Editors(int noteId, int excludeEmployeeId)
    {
        var cutoff = time.GetUtcNow() - _ttl;
        return _entries.Where(e => e.Key.NoteId == noteId && e.Key.EmployeeId != excludeEmployeeId && e.Value.LastSeen > cutoff)
            .Select(e => (e.Key.EmployeeId, e.Value.Name)).OrderBy(e => e.Name).ToList();
    }

    /// <summary>Removes expired entries and returns a "stopped editing" event for each.</summary>
    public List<NoteEvent> Sweep()
    {
        var cutoff = time.GetUtcNow() - _ttl;
        var expired = new List<NoteEvent>();
        foreach (var (key, entry) in _entries)
            if (entry.LastSeen <= cutoff && _entries.TryRemove(key, out _))
                expired.Add(entry.Template with { Active = false });
        return expired;
    }
}

/// <summary>Periodically announces that editors who stopped sending heartbeats (closed tab, lost connection) are gone.</summary>
public class PresenceSweeper(PresenceTracker presence, NoteEventBroadcaster broadcaster, IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(config.GetValue("Presence:SweepSeconds", 10)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            foreach (var expired in presence.Sweep()) broadcaster.Publish(expired);
    }
}
