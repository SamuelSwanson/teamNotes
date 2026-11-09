using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.DTOs;

// ---------- Notes ----------

public class NoteRequest
{
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required, StringLength(20000)] public string Content { get; set; } = "";
    [Required] public Visibility? Visibility { get; set; }
    /// <summary>Required when Visibility is Team; must be null otherwise.</summary>
    public int? TeamId { get; set; }
    public List<int>? TopicIds { get; set; }
}

public class UpdateNoteRequest : NoteRequest
{
    /// <summary>The Version returned when the note was last read. Mismatch => 409.</summary>
    [Required] public Guid? ExpectedVersion { get; set; }
}

public record PagedNotes(List<NoteDto> Items, int Total);

public class NoteQuery
{
    public int? TeamId { get; set; }
    public int? TopicId { get; set; }
    public int? CreatedBy { get; set; }
    public Visibility? Visibility { get; set; }
    public string? Search { get; set; }
    /// <summary>1-based page number.</summary>
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    /// <summary>Notes per page (1-100). The total is returned in the X-Total-Count header.</summary>
    [Range(1, 100)] public int PageSize { get; set; } = 50;
}

public class NoteDto
{
    public int NoteId { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public Visibility Visibility { get; set; }
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }
    public List<TopicDto> Topics { get; set; } = [];
    public int CreatedByEmployeeId { get; set; }
    public string CreatedByName { get; set; } = "";
    public int UpdatedByEmployeeId { get; set; }
    public string UpdatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid Version { get; set; }
    /// <summary>Convenience flag for clients; the server enforces the rule regardless.</summary>
    public bool CanDelete { get; set; }
    public List<AttachmentDto> Attachments { get; set; } = [];
}

public class RevisionDto
{
    public int RevisionId { get; set; }
    public string Action { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public Visibility Visibility { get; set; }
    public string? TeamName { get; set; }
    public string Topics { get; set; } = "";
    public Guid NoteVersion { get; set; }
    public int EditedByEmployeeId { get; set; }
    public string EditedByName { get; set; } = "";
    public DateTime EditedAt { get; set; }
}

public class EditorDto
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
}

// ---------- Topics ----------

public class TopicRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [StringLength(500)] public string? Description { get; set; }
}

public class TopicDto
{
    public int TopicId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

// ---------- Employees & teams ----------

public class EmployeeRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 8)] public string Password { get; set; } = "";
    public EmployeeRole? Role { get; set; }
}

public class LoginRequest
{
    [Required] public string Email { get; set; } = "";
    [Required] public string Password { get; set; } = "";
}

public class ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 8)] public string NewPassword { get; set; } = "";
}

public class TeamSummaryDto
{
    public int TeamId { get; set; }
    public string Name { get; set; } = "";
    public string? Role { get; set; }
}

public class EmployeeDto
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public EmployeeRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<TeamSummaryDto> Teams { get; set; } = [];
}

public class TeamRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
}

public class AddMemberRequest
{
    [Required] public int? EmployeeId { get; set; }
    [StringLength(50)] public string? Role { get; set; }
}

public class MemberDto
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Role { get; set; }
}

public class TeamDto
{
    public int TeamId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<MemberDto> Members { get; set; } = [];
}

public class AttachmentDto
{
    public int AttachmentId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public int UploadedByEmployeeId { get; set; }
    public string UploadedByName { get; set; } = "";
    public DateTime UploadedAt { get; set; }
}
