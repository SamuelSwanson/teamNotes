using SharedNotes.Api.Models;

namespace SharedNotes.Api.Data.Configurations;

/// <summary>Demo data inserted by the initial migration. Values are fixed so the migration is deterministic.</summary>
public static class SeedData
{
    private static readonly DateTime Seeded = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    public static readonly Employee[] Employees =
    [
        // Alice is the demo administrator. Passwords are not stored in the migration; see DemoSeeder.
        new() { EmployeeId = 1, Name = "Alice Chen", Email = "admin@notes.com", Role = EmployeeRole.Admin, CreatedAt = Seeded },
        new() { EmployeeId = 2, Name = "Bob Martinez", Email = "bob@notes.com", CreatedAt = Seeded },
        new() { EmployeeId = 3, Name = "Carol Singh", Email = "carol@notes.com", CreatedAt = Seeded },
        new() { EmployeeId = 4, Name = "Dave Okafor", Email = "dave@notes.com", CreatedAt = Seeded },
    ];

    public static readonly Team[] Teams =
    [
        new() { TeamId = 1, Name = "Engineering", CreatedAt = Seeded },
        new() { TeamId = 2, Name = "Marketing", CreatedAt = Seeded },
    ];

    // Alice belongs to both teams; Dave belongs to none.
    // Anonymous objects are used where seeding only needs FK values (no navigation properties).
    public static readonly object[] Members =
    [
        new { EmployeeId = 1, TeamId = 1, Role = (string?)"Lead" },
        new { EmployeeId = 1, TeamId = 2, Role = (string?)null },
        new { EmployeeId = 2, TeamId = 1, Role = (string?)null },
        new { EmployeeId = 3, TeamId = 2, Role = (string?)"Lead" },
    ];

    public static readonly Topic[] Topics =
    [
        new() { TopicId = 1, Name = "Planning", Description = "Roadmaps and priorities" },
        new() { TopicId = 2, Name = "Meeting Notes", Description = "Minutes and action items" },
        new() { TopicId = 3, Name = "Ideas" },
    ];

    public static readonly object[] Notes =
    [
        Note(1, "My private to-do list", "Review PRs, update docs, book travel.", 1, null, Visibility.Private, "00000000-0000-0000-0000-000000000001"),
        Note(2, "Sprint planning", "Goals for the next sprint: finish the notes API and write tests.", 2, 1, Visibility.Team, "00000000-0000-0000-0000-000000000002"),
        Note(3, "Company holiday schedule", "The office is closed on public holidays and the last Friday of each quarter.", 3, null, Visibility.All, "00000000-0000-0000-0000-000000000003"),
        Note(4, "Campaign ideas", "Spring launch: short videos, customer stories, partner webinar.", 3, 2, Visibility.Team, "00000000-0000-0000-0000-000000000004"),
    ];

    public static readonly object[] NoteTopics =
    [
        new { NoteId = 1, TopicId = 1 },
        new { NoteId = 2, TopicId = 1 },
        new { NoteId = 2, TopicId = 2 },
        new { NoteId = 4, TopicId = 3 },
    ];

    private static object Note(int id, string title, string content, int createdBy, int? teamId, Visibility visibility, string version) => new
    {
        NoteId = id,
        Title = title,
        Content = content,
        CreatedByEmployeeId = createdBy,
        UpdatedByEmployeeId = createdBy,
        TeamId = teamId,
        Visibility = visibility,
        CreatedAt = Seeded,
        UpdatedAt = Seeded,
        Version = Guid.Parse(version),
    };
}
