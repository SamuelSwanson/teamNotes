using Microsoft.AspNetCore.Mvc;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Services;

namespace SharedNotes.Api.Controllers;

/// <summary>The signed-in employee's own notifications. There is no way to read anyone else's.</summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController(NotificationService notifications) : ControllerBase
{
    /// <summary>The 50 most recent notifications, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<List<NotificationDto>>> List([FromQuery] bool unreadOnly = false) =>
        Ok(await notifications.ListAsync(unreadOnly));

    [HttpGet("unread-count")]
    public async Task<ActionResult> UnreadCount() => Ok(new { count = await notifications.UnreadCountAsync() });

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        await notifications.MarkReadAsync(id);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await notifications.MarkAllReadAsync();
        return NoContent();
    }
}
