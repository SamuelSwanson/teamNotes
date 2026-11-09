using Microsoft.AspNetCore.Mvc;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Services;

namespace SharedNotes.Api.Controllers;

[ApiController]
[Route("api/notes")]
public class NotesController(NoteService notes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<NoteDto>>> List([FromQuery] NoteQuery query) => Ok(await notes.ListAsync(query));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<NoteDto>> Get(int id) => Ok(await notes.GetAsync(id));

    /// <summary>Every saved state of the note, newest first. Same access rule as reading the note.</summary>
    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<List<RevisionDto>>> History(int id) => Ok(await notes.HistoryAsync(id));

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
