using Microsoft.AspNetCore.Mvc;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Services;

namespace SharedNotes.Api.Controllers;

[ApiController]
[Route("api/notes")]
public class NotesController(NoteService notes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<NoteDto>>> List([FromQuery] NoteQuery query)
    {
        var page = await notes.ListAsync(query);
        Response.Headers["X-Total-Count"] = page.Total.ToString();   // total matches across all pages
        Response.Headers["X-Page"] = query.Page.ToString();
        Response.Headers["X-Page-Size"] = query.PageSize.ToString();
        return Ok(page.Items);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<NoteDto>> Get(int id) => Ok(await notes.GetAsync(id));

    /// <summary>Every saved state of the note, newest first. Same access rule as reading the note.</summary>
    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<List<RevisionDto>>> History(int id) => Ok(await notes.HistoryAsync(id));

    /// <summary>Other employees who currently have this note open in the editor.</summary>
    [HttpGet("{id:int}/editors")]
    public async Task<ActionResult<List<EditorDto>>> Editors(int id) => Ok(await notes.EditorsAsync(id));

    /// <summary>Heartbeat: "I have this note open for editing". Expires after ~45s without a repeat.</summary>
    [HttpPut("{id:int}/editing")]
    public async Task<IActionResult> StartEditing(int id)
    {
        await notes.StartEditingAsync(id);
        return NoContent();
    }

    [HttpDelete("{id:int}/editing")]
    public async Task<IActionResult> StopEditing(int id)
    {
        await notes.StopEditingAsync(id);
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<NoteDto>> Create(NoteRequest request)
    {
        var note = await notes.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = note.NoteId }, note);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<NoteDto>> Update(int id, UpdateNoteRequest request) =>
        Ok(await notes.UpdateAsync(id, request));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await notes.DeleteAsync(id);
        return NoContent();
    }
}
