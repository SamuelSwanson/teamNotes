using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Middleware;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Services;

/// <summary>
/// Team shout-outs: "please review / edit / answer this". Anyone can ask any team, so people can reach teams they are
/// not in. A shout-out never grants access: if it points at a note, the target team must already be able to read it.
/// </summary>
public class ShoutOutService(AppDbContext db, CurrentEmployeeAccessor current, NoteService notes, NotificationService notifications)
{
    private const int MaxListed = 50;
    private const int MaxPerHour = 10;   // simple spam guard per sender

    private int Me => current.Required.EmployeeId;

    public async Task<ShoutOutDto> SendAsync(int teamId, ShoutOutRequest r)
    {
        var team = await db.Teams.AsNoTracking().Where(t => t.TeamId == teamId)
                       .Select(t => new { t.Name, Members = t.Members.Select(m => m.EmployeeId).ToList() }).FirstOrDefaultAsync()
                   ?? throw ApiException.NotFound($"Team {teamId} not found.");
        var message = r.Message.Trim();
        if (message.Length == 0) throw ApiException.BadRequest("A message is required.");
        if (!Enum.IsDefined(r.Kind!.Value)) throw ApiException.BadRequest("Kind must be Review, Edit, Answer or Info.");

        if (await db.ShoutOuts.CountAsync(s => s.SenderEmployeeId == Me && s.CreatedAt > DateTime.UtcNow.AddHours(-1)) >= MaxPerHour)
            throw ApiException.TooManyRequests($"You can send at most {MaxPerHour} shout-outs per hour.");

        string? noteTitle = null;
        if (r.NoteId is { } noteId)
        {
            await notes.EnsureCanAccessAsync(noteId);   // the sender must be able to read it (404 / 403)
            var shared = await db.Notes.Where(n => n.NoteId == noteId &&
                    (n.Visibility == Visibility.All || (n.Visibility == Visibility.Team && n.TeamId == teamId)))
                .Select(n => n.Title).FirstOrDefaultAsync();
            if (shared is null)
                throw ApiException.BadRequest($"{team.Name} cannot read that note. Share the note with them (Team or Everyone) before asking them to look at it.");
            noteTitle = shared;
        }

        var shoutOut = new ShoutOut
        {
            SenderEmployeeId = Me,
            TeamId = teamId,
            NoteId = r.NoteId,
            Kind = r.Kind.Value,
            Message = message,
            CreatedAt = DateTime.UtcNow,
        };
        db.ShoutOuts.Add(shoutOut);
        await db.SaveChangesAsync();

        await notifications.NotifyAsync(team.Members, NotificationTypes.ShoutOut,
            Describe(current.Required.Name, team.Name, shoutOut.Kind, noteTitle, message),
            noteId: r.NoteId, teamId: teamId, shoutOutId: shoutOut.ShoutOutId);
        return await GetDtoAsync(shoutOut.ShoutOutId);
    }

    /// <summary>box: "inbox" (sent to teams I belong to) or "sent" (sent by me). status: "open", "resolved" or "all".</summary>
    public async Task<List<ShoutOutDto>> ListAsync(string box, string status)
    {
        if (box is not ("inbox" or "sent")) throw ApiException.BadRequest("box must be 'inbox' or 'sent'.");
        if (status is not ("open" or "resolved" or "all")) throw ApiException.BadRequest("status must be 'open', 'resolved' or 'all'.");

        var query = db.ShoutOuts.AsNoTracking().AsQueryable();
        query = box == "sent"
            ? query.Where(s => s.SenderEmployeeId == Me)
            : query.Where(s => s.Team.Members.Any(m => m.EmployeeId == Me));
        if (status == "open") query = query.Where(s => s.ResolvedAt == null);
        if (status == "resolved") query = query.Where(s => s.ResolvedAt != null);

        return await ToDtosAsync(query.OrderByDescending(s => s.ShoutOutId).Take(MaxListed));
    }

    public async Task<ShoutOutDto> GetAsync(int id)
    {
        await EnsureParticipantAsync(id);
        return await GetDtoAsync(id);
    }

    /// <summary>The target team's members (or the sender) can resolve; the sender is told, optionally with a response.</summary>
    public async Task<ShoutOutDto> ResolveAsync(int id, ResolveShoutOutRequest r)
    {
        await EnsureParticipantAsync(id);
        var shoutOut = await db.ShoutOuts.Include(s => s.Team).FirstAsync(s => s.ShoutOutId == id);
        if (shoutOut.ResolvedAt is not null) throw ApiException.Conflict("This request is already resolved.");

        var response = string.IsNullOrWhiteSpace(r.Response) ? null : r.Response.Trim();
        shoutOut.ResolvedAt = DateTime.UtcNow;
        shoutOut.ResolvedByEmployeeId = Me;
        shoutOut.Response = response;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw ApiException.Conflict("This request was changed by someone else.");
        }

        await notifications.NotifyAsync([shoutOut.SenderEmployeeId], NotificationTypes.ShoutOutResolved,
            $"{current.Required.Name} resolved your request to {shoutOut.Team.Name}" + (response is null ? "." : $": {response}"),
            noteId: shoutOut.NoteId, teamId: shoutOut.TeamId, shoutOutId: id);
        return await GetDtoAsync(id);
    }

    // ---------- helpers ----------

    private async Task EnsureParticipantAsync(int id)
    {
        var info = await db.ShoutOuts.AsNoTracking().Where(s => s.ShoutOutId == id)
                       .Select(s => new { s.SenderEmployeeId, IsMember = s.Team.Members.Any(m => m.EmployeeId == Me) })
                       .FirstOrDefaultAsync()
                   ?? throw ApiException.NotFound($"Shout-out {id} not found.");
        if (info.SenderEmployeeId != Me && !info.IsMember) throw ApiException.Forbidden("This request is not addressed to you or your team.");
    }

    private async Task<ShoutOutDto> GetDtoAsync(int id) =>
        (await ToDtosAsync(db.ShoutOuts.AsNoTracking().Where(s => s.ShoutOutId == id))).First();

    private async Task<List<ShoutOutDto>> ToDtosAsync(IQueryable<ShoutOut> query)
    {
        var items = await query.Select(s => new ShoutOutDto
        {
            ShoutOutId = s.ShoutOutId,
            Kind = s.Kind,
            Message = s.Message,
            Status = s.ResolvedAt == null ? "Open" : "Resolved",
            TeamId = s.TeamId,
            TeamName = s.Team.Name,
            NoteId = s.NoteId,
            NoteTitle = s.Note != null ? s.Note.Title : null,
            SenderEmployeeId = s.SenderEmployeeId,
            SenderName = s.Sender.Name,
            CreatedAt = s.CreatedAt,
            ResolvedAt = s.ResolvedAt,
            ResolvedByName = s.ResolvedBy != null ? s.ResolvedBy.Name : null,
            Response = s.Response,
        }).ToListAsync();

        // The note's title is only shown to people who can read the note right now (visibility may have changed since).
        var readable = await notes.ReadableNoteIdsAsync(items.Where(i => i.NoteId is not null).Select(i => i.NoteId!.Value));
        foreach (var item in items.Where(i => i.NoteId is { } n && !readable.Contains(n))) item.NoteTitle = null;
        return items;
    }

    private static string Describe(string actor, string team, ShoutOutKind kind, string? noteTitle, string message)
    {
        if (kind == ShoutOutKind.Info)
            return noteTitle is null ? $"{actor} sent {team} a message: {message}" : $"{actor} pointed {team} to “{noteTitle}”: {message}";
        var verb = kind switch { ShoutOutKind.Review => "review", ShoutOutKind.Edit => "edit", _ => "answer" };
        return noteTitle is null
            ? $"{actor} asked {team} to {verb} something: {message}"
            : $"{actor} asked {team} to {verb} “{noteTitle}”: {message}";
    }
}
