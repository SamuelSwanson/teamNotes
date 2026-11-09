using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Middleware;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Services;

public static class NotificationTypes
{
    public const string NoteEdited = "note-edited";
    public const string NoteShared = "note-shared";
    public const string TeamAdded = "team-added";
    public const string ShoutOut = "shout-out";
    public const string ShoutOutResolved = "shout-out-resolved";
}

/// <summary>
/// Stores in-app notifications and pushes a live "notification" event to each recipient over the event stream.
/// Notifications are best-effort: a failure here is logged and never fails the action that triggered it.
/// </summary>
public class NotificationService(AppDbContext db, CurrentEmployeeAccessor current, NoteEventBroadcaster events, ILogger<NotificationService> logger)
{
    private const int MaxListed = 50;
    private int Me => current.Required.EmployeeId;

    /// <summary>Notifies the given employees (never the acting employee). Callers pass only people who may see the subject.</summary>
    public async Task NotifyAsync(IEnumerable<int> recipientIds, string type, string message, int? noteId = null, int? teamId = null, int? shoutOutId = null)
    {
        var actor = current.Required;
        var recipients = recipientIds.Distinct().Where(id => id != actor.EmployeeId).ToList();
        if (recipients.Count == 0) return;

        var now = DateTime.UtcNow;
        var rows = recipients.Select(id => new Notification
        {
            RecipientEmployeeId = id,
            ActorEmployeeId = actor.EmployeeId,
            Type = type,
            Message = message.Length > 500 ? message[..499] + "…" : message,
            NoteId = noteId,
            TeamId = teamId,
            ShoutOutId = shoutOutId,
            CreatedAt = now,
        }).ToList();

        try
        {
            db.Notifications.AddRange(rows);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not store {Type} notification for {Count} recipient(s)", type, rows.Count);
            foreach (var row in rows) db.Entry(row).State = EntityState.Detached;   // keep the request's DbContext usable
            return;
        }

        // The event stream only delivers Private events to their OwnerId, so this reaches exactly the recipient.
        foreach (var row in rows)
            events.Publish(new NoteEvent(NoteEvent.Notification, noteId ?? 0, Visibility.Private, null, row.RecipientEmployeeId,
                actor.EmployeeId, actor.Name));
    }

    public Task<List<NotificationDto>> ListAsync(bool unreadOnly) =>
        db.Notifications.AsNoTracking()
            .Where(n => n.RecipientEmployeeId == Me && (!unreadOnly || n.ReadAt == null))
            .OrderByDescending(n => n.NotificationId)
            .Take(MaxListed)
            .Select(n => new NotificationDto
            {
                NotificationId = n.NotificationId,
                Type = n.Type,
                Message = n.Message,
                NoteId = n.NoteId,
                TeamId = n.TeamId,
                ShoutOutId = n.ShoutOutId,
                ActorEmployeeId = n.ActorEmployeeId,
                ActorName = n.Actor.Name,
                CreatedAt = n.CreatedAt,
                IsRead = n.ReadAt != null,
            }).ToListAsync();

    public Task<int> UnreadCountAsync() => db.Notifications.CountAsync(n => n.RecipientEmployeeId == Me && n.ReadAt == null);

    public async Task MarkReadAsync(int id)
    {
        // Someone else's notification looks exactly like a missing one.
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.NotificationId == id && x.RecipientEmployeeId == Me)
                ?? throw ApiException.NotFound($"Notification {id} not found.");
        n.ReadAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public Task MarkAllReadAsync()
    {
        var now = DateTime.UtcNow;
        return db.Notifications.Where(n => n.RecipientEmployeeId == Me && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
    }
}
