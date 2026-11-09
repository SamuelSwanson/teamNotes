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
