using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Models;
using SharedNotes.Api.Services;

namespace SharedNotes.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController(NoteEventBroadcaster broadcaster, CurrentEmployeeAccessor current, IServiceScopeFactory scopes) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Server-Sent Events stream of note changes and editing presence. Each event is delivered only to employees who
    /// may read the note, and carries no note content: clients re-fetch through the normal, authorized API.
    /// </summary>
    [HttpGet]
    public async Task Stream(CancellationToken cancel)
    {
        var me = current.Required.EmployeeId;
        using var subscription = broadcaster.Subscribe();   // subscribe before the response starts so nothing is missed

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.WriteAsync(": connected\n\n", cancel);
        await Response.Body.FlushAsync(cancel);

        try
        {
            using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var next = subscription.Reader.WaitToReadAsync(cancel).AsTask();
            var tick = heartbeat.WaitForNextTickAsync(cancel).AsTask();
            while (!cancel.IsCancellationRequested)
            {
                var done = await Task.WhenAny(next, tick);
                if (done == tick)
                {
                    await Response.WriteAsync(": keep-alive\n\n", cancel);   // keeps proxies from closing an idle stream
                    tick = heartbeat.WaitForNextTickAsync(cancel).AsTask();
                }
                else
                {
                    if (!await next) break;
                    while (subscription.Reader.TryRead(out var ev))
                        if (await CanSeeAsync(ev, me)) await Write(ev);
                    next = subscription.Reader.WaitToReadAsync(cancel).AsTask();
                }
                await Response.Body.FlushAsync(cancel);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }

        async Task Write(NoteEvent ev)
        {
            var payload = JsonSerializer.Serialize(
                new { noteId = ev.NoteId, actorId = ev.ActorId, actorName = ev.ActorName, version = ev.Version, active = ev.Active }, Json);
            await Response.WriteAsync($"event: {ev.Type}\ndata: {payload}\n\n", cancel);
        }
    }

    /// <summary>Same visibility rules as reading a note: Private = creator, Team = members, All = everyone.</summary>
    private async Task<bool> CanSeeAsync(NoteEvent ev, int me)
    {
        switch (ev.Visibility)
        {
            case Visibility.All: return true;
            case Visibility.Private: return ev.OwnerId == me;
            default:
                await using (var scope = scopes.CreateAsyncScope())   // membership may change while the stream is open
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    return await db.TeamMembers.AnyAsync(m => m.TeamId == ev.TeamId && m.EmployeeId == me);
                }
        }
    }
}
