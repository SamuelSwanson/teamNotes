using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Middleware;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Services;

/// <summary>Note business logic: visibility rules, authorization, filtering and optimistic concurrency.</summary>
public class NoteService(AppDbContext db, CurrentEmployeeAccessor current)
{
    private int Me => current.Required.EmployeeId;

    public async Task<List<NoteDto>> ListAsync(NoteQuery q)
    {
        var query = Accessible(Me);

        if (q.TeamId is { } teamId) query = query.Where(n => n.TeamId == teamId);
        if (q.TopicId is { } topicId) query = query.Where(n => n.NoteTopics.Any(nt => nt.TopicId == topicId));
        if (q.CreatedBy is { } createdBy) query = query.Where(n => n.CreatedByEmployeeId == createdBy);
        if (q.Visibility is { } visibility) query = query.Where(n => n.Visibility == visibility);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(n => n.Title.ToLower().Contains(s) || n.Content.ToLower().Contains(s));
        }

        return await Project(query.OrderByDescending(n => n.UpdatedAt).ThenByDescending(n => n.NoteId), Me).ToListAsync();
    }

    public async Task<NoteDto> GetAsync(int id)
    {
        await EnsureCanAccessAsync(id);
        return await GetDtoAsync(id);
    }

    public async Task<NoteDto> CreateAsync(NoteRequest r)
    {
        var visibility = r.Visibility!.Value;
        var topicIds = await ValidateAsync(r);
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
            NoteTopics = topicIds.Select(t => new NoteTopic { TopicId = t }).ToList(),
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return await GetDtoAsync(note.NoteId);
    }

    public async Task<NoteDto> UpdateAsync(int id, UpdateNoteRequest r)
    {
        await EnsureCanAccessAsync(id);
        var topicIds = await ValidateAsync(r);
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

        var wanted = topicIds.ToHashSet();
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

        return await GetDtoAsync(id);
    }

    public async Task DeleteAsync(int id)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.NoteId == id)
                   ?? throw ApiException.NotFound($"Note {id} not found.");
        if (note.CreatedByEmployeeId != Me)
            throw ApiException.Forbidden("Only the creator can delete a note.");

        db.Notes.Remove(note);   // NoteTopics rows are removed by the database cascade
        await db.SaveChangesAsync();
    }

    // ---------- helpers ----------

    /// <summary>Notes the current employee may read and edit (Private: creator; Team: members; All: everyone).</summary>
    private IQueryable<Note> Accessible(int me) => db.Notes.Where(n =>
        n.Visibility == Visibility.All
        || (n.Visibility == Visibility.Private && n.CreatedByEmployeeId == me)
        || (n.Visibility == Visibility.Team && db.TeamMembers.Any(m => m.TeamId == n.TeamId && m.EmployeeId == me)));

    private async Task EnsureCanAccessAsync(int id)
    {
        if (await Accessible(Me).AnyAsync(n => n.NoteId == id)) return;
        if (!await db.Notes.AnyAsync(n => n.NoteId == id)) throw ApiException.NotFound($"Note {id} not found.");
        throw ApiException.Forbidden("You do not have access to this note.");
    }

    private async Task<NoteDto> GetDtoAsync(int id) =>
        await Project(db.Notes.AsNoTracking().Where(n => n.NoteId == id), Me).FirstAsync();

    /// <summary>Validates visibility/team/topic rules and returns the distinct topic ids.</summary>
    private async Task<List<int>> ValidateAsync(NoteRequest r)
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
        if (topicIds.Count > 0)
        {
            var found = await db.Topics.Where(t => topicIds.Contains(t.TopicId)).Select(t => t.TopicId).ToListAsync();
            var missing = topicIds.Except(found).ToList();
            if (missing.Count > 0) throw ApiException.BadRequest($"Unknown topic id(s): {string.Join(", ", missing)}.");
        }
        return topicIds;
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
