using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Middleware;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Services;

/// <summary>Note business logic: visibility rules, authorization, filtering and optimistic concurrency.</summary>
public class NoteService(AppDbContext db, CurrentEmployeeAccessor current, NoteEventBroadcaster events, PresenceTracker presence, AttachmentStorage attachmentStorage)
{
    private int Me => current.Required.EmployeeId;

    public async Task<PagedNotes> ListAsync(NoteQuery q)
    {
        var query = Accessible(Me);

        if (q.TeamId is { } teamId) query = query.Where(n => n.TeamId == teamId);
        if (q.TopicId is { } topicId) query = query.Where(n => n.NoteTopics.Any(nt => nt.TopicId == topicId));
        if (q.CreatedBy is { } createdBy) query = query.Where(n => n.CreatedByEmployeeId == createdBy);
        if (q.Visibility is { } visibility) query = query.Where(n => n.Visibility == visibility);
        if (!string.IsNullOrWhiteSpace(q.Search))
            return await SearchAsync(query, q);

        var total = await query.CountAsync();
        var items = await Project(query.OrderByDescending(n => n.UpdatedAt).ThenByDescending(n => n.NoteId)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize), Me).ToListAsync();
        return new PagedNotes(items, total);
    }

    private const int MaxSearchMatches = 1000;

    /// <summary>
    /// Full-text search (SQLite FTS5): stemmed, prefix-matching, best match first (title weighted above content).
    /// The index finds candidate ids; they are then intersected with the notes this employee may read, so search never
    /// reveals notes they cannot access.
    /// </summary>
    private async Task<PagedNotes> SearchAsync(IQueryable<Note> accessible, NoteQuery q)
    {
        var match = ToFtsQuery(q.Search!);
        if (match is null) return new PagedNotes([], 0);

        var ranked = await db.Database.SqlQuery<int>(
            $"SELECT rowid AS Value FROM NotesFts WHERE NotesFts MATCH {match} ORDER BY bm25(NotesFts, 5.0, 1.0) LIMIT {MaxSearchMatches}")
            .ToListAsync();
        if (ranked.Count == 0) return new PagedNotes([], 0);

        var allowed = (await accessible.Where(n => ranked.Contains(n.NoteId)).Select(n => n.NoteId).ToListAsync()).ToHashSet();
        var matches = ranked.Where(allowed.Contains).ToList();
        var ids = matches.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList();
        var byId = (await Project(db.Notes.AsNoTracking().Where(n => ids.Contains(n.NoteId)), Me).ToListAsync())
            .ToDictionary(n => n.NoteId);
        return new PagedNotes(ids.Select(id => byId[id]).ToList(), matches.Count);
    }

    /// <summary>
    /// Turns free text into a safe FTS5 query: each word becomes a quoted prefix term, all must match (AND).
    /// Quoting means user input can never be interpreted as FTS operators (NEAR, OR, column filters, ...).
    /// </summary>
    internal static string? ToFtsQuery(string search)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(search, @"[\p{L}\p{N}]+")
            .Select(m => m.Value).Take(10).ToList();
        return words.Count == 0 ? null : string.Join(' ', words.Select(w => $"\"{w}\"*"));
    }

    public async Task<NoteDto> GetAsync(int id)
    {
        await EnsureCanAccessAsync(id);
        return await GetDtoAsync(id);
    }

    public async Task<NoteDto> CreateAsync(NoteRequest r)
    {
        var visibility = r.Visibility!.Value;
        var topics = await ValidateAsync(r);
        var now = DateTime.UtcNow;

        var note = new Note
        {
            Title = r.Title.Trim(),
            Content = r.Content,
            Visibility = visibility,
            TeamId = r.TeamId,
            CreatedByEmployeeId = Me,   // identity always comes from the resolved employee, never the body
            UpdatedByEmployeeId = Me,
            CreatedAt = now,
            UpdatedAt = now,
            Version = Guid.NewGuid(),
            NoteTopics = topics.Select(t => new NoteTopic { TopicId = t.TopicId }).ToList(),
        };
        note.Revisions.Add(Snapshot(note, "Created", await TeamNameAsync(note.TeamId), topics));
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        Publish(NoteEvent.Changed, note);
        return await GetDtoAsync(note.NoteId);
    }

    public async Task<NoteDto> UpdateAsync(int id, UpdateNoteRequest r)
    {
        await EnsureCanAccessAsync(id);
        var topics = await ValidateAsync(r);
        var note = await db.Notes.Include(n => n.NoteTopics).FirstAsync(n => n.NoteId == id);

        var visibility = r.Visibility!.Value;
        if ((note.Visibility != visibility || note.TeamId != r.TeamId) && note.CreatedByEmployeeId != Me)
            throw ApiException.Forbidden("Only the creator can change a note's visibility or team.");

        note.Title = r.Title.Trim();
        note.Content = r.Content;
        note.Visibility = visibility;
        note.TeamId = r.TeamId;
        note.UpdatedAt = DateTime.UtcNow;
        note.UpdatedByEmployeeId = Me;

        var wanted = topics.Select(t => t.TopicId).ToHashSet();
        foreach (var link in note.NoteTopics.Where(nt => !wanted.Contains(nt.TopicId)).ToList())
            note.NoteTopics.Remove(link);
        foreach (var topicId in wanted.Where(t => note.NoteTopics.All(nt => nt.TopicId != t)))
            note.NoteTopics.Add(new NoteTopic { TopicId = topicId });

        // Optimistic concurrency: tell EF the version the client saw. SaveChanges then issues
        //   UPDATE Notes SET ..., Version = @new WHERE NoteId = @id AND Version = @expected
        // as one atomic statement; if another request changed the row first, zero rows match
        // and EF throws DbUpdateConcurrencyException. The check does not depend on the earlier read.
        db.Entry(note).Property(n => n.Version).OriginalValue = r.ExpectedVersion!.Value;
        note.Version = Guid.NewGuid();
        note.Revisions.Add(Snapshot(note, "Updated", await TeamNameAsync(note.TeamId), topics));

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Includes DbUpdateConcurrencyException. A constraint failure here (e.g. a topic deleted or the same
            // link inserted by a racing request after our validation) also means someone changed data concurrently.
            throw ApiException.Conflict("The note was modified by someone else. Reload it and try again.");
        }

        Publish(NoteEvent.Changed, note);
        return await GetDtoAsync(id);
    }

    public async Task DeleteAsync(int id)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.NoteId == id)
                   ?? throw ApiException.NotFound($"Note {id} not found.");
        if (note.CreatedByEmployeeId != Me)
            throw ApiException.Forbidden("Only the creator can delete a note.");

        var files = await db.NoteAttachments.Where(a => a.NoteId == id).Select(a => a.StoredName).ToListAsync();
        db.Notes.Remove(note);   // NoteTopics, revisions and attachment rows are removed by the database cascade
        await db.SaveChangesAsync();
        foreach (var stored in files) attachmentStorage.Delete(stored);   // after the commit, so a failed delete never loses files
        Publish(NoteEvent.Deleted, note);
    }

    // ---------- editing presence ----------

    /// <summary>Marks the current employee as having the note open for editing (call again every ~20s as a heartbeat).</summary>
    public async Task StartEditingAsync(int id)
    {
        await EnsureCanAccessAsync(id);
        var note = await db.Notes.AsNoTracking().FirstAsync(n => n.NoteId == id);
        var template = new NoteEvent(NoteEvent.Editing, id, note.Visibility, note.TeamId, note.CreatedByEmployeeId,
            Me, current.Required.Name, Active: true);
        if (presence.Touch(id, Me, current.Required.Name, template)) events.Publish(template);
    }

    public async Task StopEditingAsync(int id)
    {
        if (!presence.Remove(id, Me)) return;
        var note = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.NoteId == id);
        if (note is null) return;   // note was deleted meanwhile; nothing to announce
        events.Publish(new NoteEvent(NoteEvent.Editing, id, note.Visibility, note.TeamId, note.CreatedByEmployeeId,
            Me, current.Required.Name, Active: false));
    }

    /// <summary>Other employees currently editing the note.</summary>
    public async Task<List<EditorDto>> EditorsAsync(int id)
    {
        await EnsureCanAccessAsync(id);
        return presence.Editors(id, Me).Select(e => new EditorDto { EmployeeId = e.EmployeeId, Name = e.Name }).ToList();
    }

    /// <summary>Lets other clients know the note changed in a way that does not touch its Version (e.g. attachments).</summary>
    public async Task NotifyChangedAsync(int id)
    {
        var note = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.NoteId == id);
        if (note is not null) Publish(NoteEvent.Changed, note);
    }

    private void Publish(string type, Note note) =>
        events.Publish(new NoteEvent(type, note.NoteId, note.Visibility, note.TeamId, note.CreatedByEmployeeId,
            Me, current.Required.Name, note.Version));

    // ---------- helpers ----------

    /// <summary>Notes the current employee may read and edit (Private: creator; Team: members; All: everyone).</summary>
    private IQueryable<Note> Accessible(int me) => db.Notes.Where(n =>
        n.Visibility == Visibility.All
        || (n.Visibility == Visibility.Private && n.CreatedByEmployeeId == me)
        || (n.Visibility == Visibility.Team && db.TeamMembers.Any(m => m.TeamId == n.TeamId && m.EmployeeId == me)));

    /// <summary>Throws 404 if the note does not exist and 403 if the current employee may not read/edit it.</summary>
    public async Task EnsureCanAccessAsync(int id)
    {
        if (await Accessible(Me).AnyAsync(n => n.NoteId == id)) return;
        if (!await db.Notes.AnyAsync(n => n.NoteId == id)) throw ApiException.NotFound($"Note {id} not found.");
        throw ApiException.Forbidden("You do not have access to this note.");
    }

    private async Task<NoteDto> GetDtoAsync(int id) =>
        await Project(db.Notes.AsNoTracking().Where(n => n.NoteId == id), Me).FirstAsync();

    /// <summary>Validates visibility/team/topic rules and returns the distinct topic ids.</summary>
    private async Task<List<Topic>> ValidateAsync(NoteRequest r)
    {
        var visibility = r.Visibility!.Value;
        if (!Enum.IsDefined(visibility)) throw ApiException.BadRequest("Visibility must be Private, Team or All.");
        if (string.IsNullOrWhiteSpace(r.Title)) throw ApiException.BadRequest("Title is required.");

        if (visibility == Visibility.Team)
        {
            if (r.TeamId is not { } teamId) throw ApiException.BadRequest("TeamId is required when Visibility is Team.");
            if (!await db.Teams.AnyAsync(t => t.TeamId == teamId)) throw ApiException.BadRequest($"Team {teamId} does not exist.");
            if (!await db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.EmployeeId == Me))
                throw ApiException.Forbidden("You can only use teams you belong to for Team notes.");
        }
        else if (r.TeamId is not null)
        {
            throw ApiException.BadRequest("TeamId must be null unless Visibility is Team.");
        }

        var topicIds = (r.TopicIds ?? []).Distinct().ToList();
        if (topicIds.Count == 0) return [];

        var found = await db.Topics.AsNoTracking().Where(t => topicIds.Contains(t.TopicId)).ToListAsync();
        var missing = topicIds.Except(found.Select(t => t.TopicId)).ToList();
        if (missing.Count > 0) throw ApiException.BadRequest($"Unknown topic id(s): {string.Join(", ", missing)}.");
        return found;
    }

    /// <summary>
    /// Records the note's state after a create/update. It is saved in the same transaction as the note change, so a
    /// conflicting update (which rolls back) leaves no history row behind.
    /// </summary>
    private NoteRevision Snapshot(Note note, string action, string? teamName, IEnumerable<Topic> topics) => new()
    {
        Action = action,
        Title = note.Title,
        Content = note.Content,
        Visibility = note.Visibility,
        TeamName = teamName,
        Topics = string.Join(", ", topics.Select(t => t.Name).Order()),
        NoteVersion = note.Version,
        EditedByEmployeeId = Me,
        EditedAt = note.UpdatedAt,
    };

    private Task<string?> TeamNameAsync(int? teamId) =>
        teamId is null ? Task.FromResult<string?>(null)
            : db.Teams.Where(t => t.TeamId == teamId).Select(t => (string?)t.Name).FirstOrDefaultAsync();

    public async Task<List<RevisionDto>> HistoryAsync(int id)
    {
        await EnsureCanAccessAsync(id);
        return await db.NoteRevisions.AsNoTracking().Where(r => r.NoteId == id)
            .OrderByDescending(r => r.RevisionId)
            .Select(r => new RevisionDto
            {
                RevisionId = r.RevisionId,
                Action = r.Action,
                Title = r.Title,
                Content = r.Content,
                Visibility = r.Visibility,
                TeamName = r.TeamName,
                Topics = r.Topics,
                NoteVersion = r.NoteVersion,
                EditedByEmployeeId = r.EditedByEmployeeId,
                EditedByName = r.EditedBy.Name,
                EditedAt = r.EditedAt,
            }).ToListAsync();
    }

    private static IQueryable<NoteDto> Project(IQueryable<Note> q, int me) => q.Select(n => new NoteDto
    {
        NoteId = n.NoteId,
        Title = n.Title,
        Content = n.Content,
        Visibility = n.Visibility,
        TeamId = n.TeamId,
        TeamName = n.Team != null ? n.Team.Name : null,
        Topics = n.NoteTopics.OrderBy(nt => nt.Topic.Name)
            .Select(nt => new TopicDto { TopicId = nt.TopicId, Name = nt.Topic.Name, Description = nt.Topic.Description })
            .ToList(),
        Attachments = n.Attachments.OrderBy(a => a.AttachmentId).Select(a => new AttachmentDto
        {
            AttachmentId = a.AttachmentId, FileName = a.FileName, ContentType = a.ContentType, SizeBytes = a.SizeBytes,
            UploadedByEmployeeId = a.UploadedByEmployeeId, UploadedByName = a.UploadedBy.Name, UploadedAt = a.UploadedAt,
        }).ToList(),
        CreatedByEmployeeId = n.CreatedByEmployeeId,
        CreatedByName = n.CreatedBy.Name,
        UpdatedByEmployeeId = n.UpdatedByEmployeeId,
        UpdatedByName = n.UpdatedBy.Name,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt,
        Version = n.Version,
        CanDelete = n.CreatedByEmployeeId == me,
    });
}
