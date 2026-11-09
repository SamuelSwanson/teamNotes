using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SharedNotes.Tests;

public record Attachment(int AttachmentId, string FileName, string ContentType, long SizeBytes, int UploadedByEmployeeId, string UploadedByName);

public class AttachmentTests : IDisposable
{
    private readonly AppFactory _factory = new();
    private readonly HttpClient _alice;

    // Smallest useful signatures for each binary type.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4 minimal");

    public AttachmentTests() => _alice = _factory.ClientFor(1);

    public void Dispose() => _factory.Dispose();

    private static MultipartFormDataContent Form(string fileName, byte[] bytes, string clientContentType = "application/octet-stream")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(clientContentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private Task<HttpResponseMessage> UploadAsync(HttpClient c, int noteId, string fileName, byte[] bytes, string contentType = "application/octet-stream") =>
        c.PostAsync($"/api/notes/{noteId}/attachments", Form(fileName, bytes, contentType));

    private int StoredFileCount() => Directory.Exists(_factory.AttachmentsDir) ? Directory.GetFiles(_factory.AttachmentsDir).Length : 0;

    [Fact]
    public async Task Upload_ThenDownload_RoundTrips_WithSafeHeaders()
    {
        var note = await _alice.CreateNoteAsync();
        var created = await (await UploadAsync(_alice, note.NoteId, "diagram.png", Png)).ReadAsync<Attachment>(HttpStatusCode.Created);
        Assert.Equal("diagram.png", created.FileName);
        Assert.Equal("image/png", created.ContentType);
        Assert.Equal(Png.Length, created.SizeBytes);
        Assert.Equal(1, created.UploadedByEmployeeId);

        var download = await _alice.GetAsync($"/api/notes/{note.NoteId}/attachments/{created.AttachmentId}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Png, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("diagram.png", download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Note_ListsItsAttachments()
    {
        var note = await _alice.CreateNoteAsync();
        await UploadAsync(_alice, note.NoteId, "a.txt", "hello"u8.ToArray());
        await UploadAsync(_alice, note.NoteId, "b.pdf", Pdf);

        var fetched = await (await _alice.GetAsync($"/api/notes/{note.NoteId}")).ReadAsync<System.Text.Json.JsonElement>();
        Assert.Equal(["a.txt", "b.pdf"], fetched.GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("fileName").GetString()));
        var listed = await (await _alice.GetAsync($"/api/notes/{note.NoteId}/attachments")).ReadAsync<List<Attachment>>();
        Assert.Equal(2, listed.Count);
    }

    [Theory]
    [InlineData("page.html")]
    [InlineData("image.svg")]
    [InlineData("tool.exe")]
    [InlineData("script.js")]
    [InlineData("noextension")]
    public async Task DisallowedFileTypes_AreRejected(string fileName)
    {
        var note = await _alice.CreateNoteAsync();
        var response = await UploadAsync(_alice, note.NoteId, fileName, "<script>alert(1)</script>"u8.ToArray(), "text/html");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, StoredFileCount());
    }

    [Fact]
    public async Task ContentMustMatchTheExtension()
    {
        var note = await _alice.CreateNoteAsync();
        // HTML pretending to be a PNG, and binary data pretending to be text.
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(_alice, note.NoteId, "evil.png", "<html></html>"u8.ToArray(), "image/png")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(_alice, note.NoteId, "data.txt", [65, 0, 66])).StatusCode);
        Assert.Equal(0, StoredFileCount());
    }

    [Fact]
    public async Task ClientSuppliedContentType_IsIgnored()
    {
        var note = await _alice.CreateNoteAsync();
        var created = await (await UploadAsync(_alice, note.NoteId, "notes.txt", "plain"u8.ToArray(), "text/html")).ReadAsync<Attachment>(HttpStatusCode.Created);
        Assert.Equal("text/plain", created.ContentType);
    }

    [Fact]
    public async Task TooLargeOrEmptyFiles_AreRejected()
    {
        var note = await _alice.CreateNoteAsync();
        var big = new byte[5 * 1024 * 1024 + 1];
        "%PDF"u8.CopyTo(big);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(_alice, note.NoteId, "big.pdf", big)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(_alice, note.NoteId, "empty.txt", [])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _alice.PostAsync($"/api/notes/{note.NoteId}/attachments", new MultipartFormDataContent())).StatusCode);

        var exactlyMax = new byte[5 * 1024 * 1024];
        "%PDF"u8.CopyTo(exactlyMax);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(_alice, note.NoteId, "max.pdf", exactlyMax)).StatusCode);
    }

    [Fact]
    public async Task AtMostTenAttachmentsPerNote()
    {
        var note = await _alice.CreateNoteAsync();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Created, (await UploadAsync(_alice, note.NoteId, $"f{i}.txt", "x"u8.ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(_alice, note.NoteId, "eleven.txt", "x"u8.ToArray())).StatusCode);
    }

    [Theory]
    [InlineData("../../evil.txt", "evil.txt")]
    [InlineData("..\\..\\windows\\evil.txt", "evil.txt")]
    [InlineData("a<b>:c|d.txt", "abcd.txt")]
    public async Task HostileFileNames_AreSanitized_AndNothingIsWrittenOutsideTheStore(string hostile, string expected)
    {
        var note = await _alice.CreateNoteAsync();
        var created = await (await UploadAsync(_alice, note.NoteId, hostile, "x"u8.ToArray())).ReadAsync<Attachment>(HttpStatusCode.Created);
        Assert.Equal(expected, created.FileName);

        var stored = Directory.GetFiles(_factory.AttachmentsDir).Single();
        Assert.Matches("^[0-9a-f]{32}$", Path.GetFileName(stored));   // random name on disk, unrelated to the upload name
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "evil.txt")));
    }

    [Fact]
    public async Task Attachments_FollowTheNotesAccessRules()
    {
        // Seed note 2 is the Engineering team note; Carol (Marketing only) must not see or add files.
        var bob = _factory.ClientFor(2);
        var created = await (await UploadAsync(bob, 2, "plan.txt", "secret plan"u8.ToArray())).ReadAsync<Attachment>(HttpStatusCode.Created);

        var carol = _factory.ClientFor(3);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(carol, 2, "x.txt", "x"u8.ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await carol.GetAsync($"/api/notes/2/attachments/{created.AttachmentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await carol.GetAsync("/api/notes/2/attachments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await carol.DeleteAsync($"/api/notes/2/attachments/{created.AttachmentId}")).StatusCode);

        // A file id from another note cannot be fetched through a note you can read.
        Assert.Equal(HttpStatusCode.NotFound, (await _alice.GetAsync($"/api/notes/3/attachments/{created.AttachmentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _alice.GetAsync("/api/notes/9999/attachments/1")).StatusCode);
    }

    [Fact]
    public async Task Upload_RequiresSignIn()
    {
        var response = await UploadAsync(_factory.ClientFor(null), 3, "a.txt", "x"u8.ToArray());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OnlyUploaderOrNoteCreator_CanRemove()
    {
        var note = await _alice.CreateNoteAsync();   // an "All" note created by Alice
        var bob = _factory.ClientFor(2);
        var carol = _factory.ClientFor(3);
        var bobsFile = await (await UploadAsync(bob, note.NoteId, "bob.txt", "x"u8.ToArray())).ReadAsync<Attachment>(HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.Forbidden, (await carol.DeleteAsync($"/api/notes/{note.NoteId}/attachments/{bobsFile.AttachmentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _alice.DeleteAsync($"/api/notes/{note.NoteId}/attachments/{bobsFile.AttachmentId}")).StatusCode);   // note creator
        Assert.Equal(0, StoredFileCount());

        var carolsFile = await (await UploadAsync(carol, note.NoteId, "carol.txt", "x"u8.ToArray())).ReadAsync<Attachment>(HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent, (await carol.DeleteAsync($"/api/notes/{note.NoteId}/attachments/{carolsFile.AttachmentId}")).StatusCode);   // uploader
    }

    [Fact]
    public async Task DeletingTheNote_RemovesItsFilesFromDisk()
    {
        var note = await _alice.CreateNoteAsync();
        await UploadAsync(_alice, note.NoteId, "a.txt", "x"u8.ToArray());
        await UploadAsync(_alice, note.NoteId, "b.png", Png);
        Assert.Equal(2, StoredFileCount());

        await _alice.DeleteAsync($"/api/notes/{note.NoteId}");
        Assert.Equal(0, StoredFileCount());
    }

    [Fact]
    public async Task Attachments_DoNotChangeTheNoteVersion_SoEditorsAreNotInterrupted()
    {
        var note = await _alice.CreateNoteAsync();
        await UploadAsync(_factory.ClientFor(2), note.NoteId, "a.txt", "x"u8.ToArray());
        var update = await _alice.PutJsonAsync($"/api/notes/{note.NoteId}", HttpExtensions.UpdateBody(note, note.Version, title: "still saves"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
    }

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        var response = await _alice.GetAsync("/api/notes");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
