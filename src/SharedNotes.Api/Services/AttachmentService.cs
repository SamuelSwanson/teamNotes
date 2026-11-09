using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Middleware;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Services;

/// <summary>
/// Stores attachment bytes on the local disk under random GUID names. The original file name is only metadata, so a
/// hostile name can never influence where a file is written or read.
/// </summary>
public class AttachmentStorage(IConfiguration config, IWebHostEnvironment env)
{
    private readonly string _root = Path.GetFullPath(
        Path.Combine(env.ContentRootPath, config["Attachments:Path"] ?? Path.Combine("App_Data", "attachments")));

    private string PathFor(string storedName)
    {
        if (storedName.Length != 32 || !storedName.All(Uri.IsHexDigit))   // defence in depth: only our own GUID names
            throw new InvalidOperationException("Invalid stored attachment name.");
        return Path.Combine(_root, storedName);
    }

    public async Task SaveAsync(string storedName, Stream content, CancellationToken cancel)
    {
        Directory.CreateDirectory(_root);
        await using var file = new FileStream(PathFor(storedName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancel);
    }

    public Stream? Open(string storedName)
    {
        var path = PathFor(storedName);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    public void Delete(string storedName)
    {
        try { File.Delete(PathFor(storedName)); }
        catch (IOException) { /* best effort: an undeletable orphan file is harmless and unreachable */ }
    }
}

public class AttachmentService(AppDbContext db, CurrentEmployeeAccessor current, NoteService notes, AttachmentStorage storage)
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxPerNote = 10;

    /// <summary>Allow-list: extension → content type served on download. Anything else (html, svg, exe, ...) is refused.</summary>
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".csv"] = "text/csv",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    // File signatures for binary formats; the content must start with one of these for its extension.
    private static readonly Dictionary<string, byte[][]> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = [[0x25, 0x50, 0x44, 0x46]],
        [".png"] = [[0x89, 0x50, 0x4E, 0x47]],
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".gif"] = [[0x47, 0x49, 0x46, 0x38]],
        [".docx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".xlsx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".pptx"] = [[0x50, 0x4B, 0x03, 0x04]],
    };

    private int Me => current.Required.EmployeeId;

    public async Task<List<AttachmentDto>> ListAsync(int noteId)
    {
        await notes.EnsureCanAccessAsync(noteId);
        return await db.NoteAttachments.AsNoTracking().Where(a => a.NoteId == noteId)
            .OrderBy(a => a.AttachmentId).Select(ToDto).ToListAsync();
    }

    public async Task<AttachmentDto> UploadAsync(int noteId, IFormFile? file, CancellationToken cancel)
    {
        await notes.EnsureCanAccessAsync(noteId);   // anyone who can edit the note may attach files
        if (file is null || file.Length == 0) throw ApiException.BadRequest("Choose a non-empty file to upload (form field 'file').");
        if (file.Length > MaxBytes) throw ApiException.BadRequest($"Files can be at most {MaxBytes / (1024 * 1024)} MB.");
        if (await db.NoteAttachments.CountAsync(a => a.NoteId == noteId, cancel) >= MaxPerNote)
            throw ApiException.BadRequest($"A note can have at most {MaxPerNote} attachments.");

        var fileName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(fileName);
        if (!AllowedTypes.TryGetValue(extension, out var contentType))
            throw ApiException.BadRequest($"File type '{extension}' is not allowed. Allowed: {string.Join(", ", AllowedTypes.Keys.Order())}.");

        await using (var probe = file.OpenReadStream())
            if (!await LooksLikeAsync(probe, extension, cancel))
                throw ApiException.BadRequest("The file's content does not match its extension.");

        var attachment = new NoteAttachment
        {
            NoteId = noteId,
            FileName = fileName,
            ContentType = contentType,   // from our allow-list, not from the client
            SizeBytes = file.Length,
            StoredName = Guid.NewGuid().ToString("N"),
            UploadedByEmployeeId = Me,
            UploadedAt = DateTime.UtcNow,
        };

        await using (var content = file.OpenReadStream())
            await storage.SaveAsync(attachment.StoredName, content, cancel);
        try
        {
            db.NoteAttachments.Add(attachment);
            await db.SaveChangesAsync(cancel);
        }
        catch
        {
            storage.Delete(attachment.StoredName);   // do not leave a file without a database row
            throw;
        }

        await notes.NotifyChangedAsync(noteId);
        return await db.NoteAttachments.AsNoTracking().Where(a => a.AttachmentId == attachment.AttachmentId).Select(ToDto).FirstAsync(cancel);
    }

    /// <summary>Returns the metadata and an open stream. The caller must dispose the stream.</summary>
    public async Task<(NoteAttachment Attachment, Stream Content)> OpenAsync(int noteId, int attachmentId)
    {
        await notes.EnsureCanAccessAsync(noteId);
        var attachment = await db.NoteAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.NoteId == noteId && a.AttachmentId == attachmentId)
                         ?? throw ApiException.NotFound($"Attachment {attachmentId} not found.");
        var stream = storage.Open(attachment.StoredName) ?? throw ApiException.NotFound("The attachment's file is missing.");
        return (attachment, stream);
    }

    /// <summary>The uploader or the note's creator may remove an attachment.</summary>
    public async Task DeleteAsync(int noteId, int attachmentId)
    {
        await notes.EnsureCanAccessAsync(noteId);
        var attachment = await db.NoteAttachments.Include(a => a.Note)
                             .FirstOrDefaultAsync(a => a.NoteId == noteId && a.AttachmentId == attachmentId)
                         ?? throw ApiException.NotFound($"Attachment {attachmentId} not found.");
        if (attachment.UploadedByEmployeeId != Me && attachment.Note.CreatedByEmployeeId != Me)
            throw ApiException.Forbidden("Only the uploader or the note's creator can remove an attachment.");

        db.NoteAttachments.Remove(attachment);
        await db.SaveChangesAsync();
        storage.Delete(attachment.StoredName);
        await notes.NotifyChangedAsync(noteId);
    }

    // ---------- helpers ----------

    /// <summary>Keeps only a safe display name: no directories, no control or reserved characters, bounded length.</summary>
    internal static string SanitizeFileName(string? raw)
    {
        var name = Path.GetFileName((raw ?? "").Replace('\\', '/'));   // drops any directory part, including ../ tricks
        var invalid = Path.GetInvalidFileNameChars().Concat(['"', '<', '>', '|', ':', '*', '?']).ToHashSet();
        name = new string(name.Where(c => !char.IsControl(c) && !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
        if (name.Length > 150)
        {
            var ext = Path.GetExtension(name);
            name = name[..(150 - ext.Length)] + ext;
        }
        return name.Length == 0 ? "file" : name;
    }

    private static async Task<bool> LooksLikeAsync(Stream stream, string extension, CancellationToken cancel)
    {
        var buffer = new byte[8192];
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancel);
        var head = buffer.AsMemory(0, read);   // Memory (not Span) so it can be used inside the lambda below

        if (Signatures.TryGetValue(extension, out var signatures))
            return signatures.Any(s => head.Span.StartsWith(s));

        return !head.Span.Contains((byte)0);   // text formats (.txt, .md, .csv) must not contain NUL bytes
    }

    private static readonly System.Linq.Expressions.Expression<Func<NoteAttachment, AttachmentDto>> ToDto = a => new AttachmentDto
    {
        AttachmentId = a.AttachmentId,
        FileName = a.FileName,
        ContentType = a.ContentType,
        SizeBytes = a.SizeBytes,
        UploadedByEmployeeId = a.UploadedByEmployeeId,
        UploadedByName = a.UploadedBy.Name,
        UploadedAt = a.UploadedAt,
    };
}
