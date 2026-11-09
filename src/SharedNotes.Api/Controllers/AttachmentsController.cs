using Microsoft.AspNetCore.Mvc;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Services;

namespace SharedNotes.Api.Controllers;

[ApiController]
[Route("api/notes/{noteId:int}/attachments")]
public class AttachmentsController(AttachmentService attachments) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AttachmentDto>>> List(int noteId) => Ok(await attachments.ListAsync(noteId));

    /// <summary>Upload one file (multipart/form-data, field name "file"). Limits and allowed types are enforced by the service.</summary>
    [HttpPost]
    [RequestSizeLimit(AttachmentService.MaxBytes + 1024 * 1024)]   // headroom for multipart overhead; the service enforces the exact file limit
    [RequestFormLimits(MultipartBodyLengthLimit = AttachmentService.MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<AttachmentDto>> Upload(int noteId, IFormFile? file, CancellationToken cancel)
    {
        var created = await attachments.UploadAsync(noteId, file, cancel);
        return CreatedAtAction(nameof(Download), new { noteId, attachmentId = created.AttachmentId }, created);
    }

    /// <summary>
    /// Always served as a download with the content type fixed by our allow-list and nosniff, so an uploaded file can
    /// never be rendered as a web page or script in the application's origin.
    /// </summary>
    [HttpGet("{attachmentId:int}")]
    public async Task<IActionResult> Download(int noteId, int attachmentId)
    {
        var (attachment, content) = await attachments.OpenAsync(noteId, attachmentId);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(content, attachment.ContentType, attachment.FileName);   // fileDownloadName => Content-Disposition: attachment
    }

    [HttpDelete("{attachmentId:int}")]
    public async Task<IActionResult> Delete(int noteId, int attachmentId)
    {
        await attachments.DeleteAsync(noteId, attachmentId);
        return NoContent();
    }
}
