using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<NoteTopic> NoteTopics => Set<NoteTopic>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite stores DateTime as text without a kind; always read values back as UTC
        // so the JSON output carries a "Z" suffix.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }
}

public class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
