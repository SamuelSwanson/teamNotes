namespace SharedNotes.Api.Models;

public enum Visibility { Private, Team, All }

public enum EmployeeRole { Employee, Admin }

public class Employee
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>PBKDF2 hash (see PasswordHasher). Null means the account cannot sign in with a password.</summary>
    public string? PasswordHash { get; set; }
    public EmployeeRole Role { get; set; } = EmployeeRole.Employee;
    public DateTime CreatedAt { get; set; }
    public List<TeamMember> Memberships { get; set; } = [];
}

public class Team
{
    public int TeamId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<TeamMember> Members { get; set; } = [];
}

public class TeamMember
{
    public int EmployeeId { get; set; }
    public int TeamId { get; set; }
    public string? Role { get; set; }
    public Employee Employee { get; set; } = null!;
    public Team Team { get; set; } = null!;
}

public class Topic
{
    public int TopicId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public class Note
{
    public int NoteId { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public int CreatedByEmployeeId { get; set; }
    public int UpdatedByEmployeeId { get; set; }
    public int? TeamId { get; set; }
    public Visibility Visibility { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Application-managed optimistic concurrency token. Regenerated on every update.</summary>
    public Guid Version { get; set; }

    public Employee CreatedBy { get; set; } = null!;
    public Employee UpdatedBy { get; set; } = null!;
    public Team? Team { get; set; }
    public List<NoteTopic> NoteTopics { get; set; } = [];
    public List<NoteRevision> Revisions { get; set; } = [];
    public List<NoteAttachment> Attachments { get; set; } = [];
}

/// <summary>Immutable snapshot of a note after a create or update (one row per change). Deleted with its note.</summary>
public class NoteRevision
{
    public int RevisionId { get; set; }
    public int NoteId { get; set; }
    public string Action { get; set; } = "";   // "Created" or "Updated"
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public Visibility Visibility { get; set; }
    public string? TeamName { get; set; }
    public string Topics { get; set; } = "";   // topic names, comma-separated, as they were at the time
    public Guid NoteVersion { get; set; }
    public int EditedByEmployeeId { get; set; }
    public DateTime EditedAt { get; set; }

    public Note Note { get; set; } = null!;
    public Employee EditedBy { get; set; } = null!;
}

public class NoteTopic
{
    public int NoteId { get; set; }
    public int TopicId { get; set; }
    public Note Note { get; set; } = null!;
    public Topic Topic { get; set; } = null!;
}

/// <summary>Metadata for a file attached to a note. The bytes live on disk under <see cref="StoredName"/>.</summary>
public class NoteAttachment
{
    public int AttachmentId { get; set; }
    public int NoteId { get; set; }
    /// <summary>Sanitized original name, for display and downloads only. Never used as a path.</summary>
    public string FileName { get; set; } = "";
    /// <summary>Derived from the (allow-listed) extension, never trusted from the client.</summary>
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    /// <summary>Random GUID file name on disk.</summary>
    public string StoredName { get; set; } = "";
    public int UploadedByEmployeeId { get; set; }
    public DateTime UploadedAt { get; set; }

    public Note Note { get; set; } = null!;
    public Employee UploadedBy { get; set; } = null!;
}

/// <summary>An in-app message for one employee (note edited, added to a team, review request, ...).</summary>
public class Notification
{
    public int NotificationId { get; set; }
    public int RecipientEmployeeId { get; set; }
    public int ActorEmployeeId { get; set; }
    public string Type { get; set; } = "";
    /// <summary>Fixed at creation time so it stays readable even if the note is later renamed or deleted.</summary>
    public string Message { get; set; } = "";
    public int? NoteId { get; set; }
    public int? TeamId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }

    public Employee Recipient { get; set; } = null!;
    public Employee Actor { get; set; } = null!;
    public Note? Note { get; set; }
    public Team? Team { get; set; }
}
