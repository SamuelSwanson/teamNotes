namespace SharedNotes.Api.Models;

public enum Visibility { Private, Team, All }

public class Employee
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
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
}

public class NoteTopic
{
    public int NoteId { get; set; }
    public int TopicId { get; set; }
    public Note Note { get; set; } = null!;
    public Topic Topic { get; set; } = null!;
}
