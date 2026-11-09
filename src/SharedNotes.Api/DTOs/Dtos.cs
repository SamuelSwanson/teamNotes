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

public class NoteQuery
{
    public int? TeamId { get; set; }
    public int? TopicId { get; set; }
    public int? CreatedBy { get; set; }
    public Visibility? Visibility { get; set; }
    public string? Search { get; set; }
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
