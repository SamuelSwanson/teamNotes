using Microsoft.AspNetCore.Mvc;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Services;

namespace SharedNotes.Api.Controllers;

[ApiController]
public class ShoutOutsController(ShoutOutService shoutOuts) : ControllerBase
{
    /// <summary>Ask a team (any team, not only your own) to review, edit or answer something.</summary>
    [HttpPost("api/teams/{teamId:int}/shoutouts")]
    public async Task<ActionResult<ShoutOutDto>> Send(int teamId, ShoutOutRequest request)
    {
        var created = await shoutOuts.SendAsync(teamId, request);
        return CreatedAtAction(nameof(Get), new { id = created.ShoutOutId }, created);
    }

    /// <summary>box=inbox (addressed to your teams, default) or sent; status=open (default), resolved or all.</summary>
    [HttpGet("api/shoutouts")]
    public async Task<ActionResult<List<ShoutOutDto>>> List([FromQuery] string box = "inbox", [FromQuery] string status = "open") =>
        Ok(await shoutOuts.ListAsync(box, status));

    [HttpGet("api/shoutouts/{id:int}")]
    public async Task<ActionResult<ShoutOutDto>> Get(int id) => Ok(await shoutOuts.GetAsync(id));

    [HttpPost("api/shoutouts/{id:int}/resolve")]
    public async Task<ActionResult<ShoutOutDto>> Resolve(int id, ResolveShoutOutRequest? request) =>
        Ok(await shoutOuts.ResolveAsync(id, request ?? new ResolveShoutOutRequest()));
}
