using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Data.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.Property(e => e.Name).IsRequired().HasMaxLength(100);
        b.Property(e => e.Email).IsRequired().HasMaxLength(254).UseCollation("NOCASE");
        b.HasIndex(e => e.Email).IsUnique();
        b.HasData(SeedData.Employees);
    }
}

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> b)
    {
        b.Property(t => t.Name).IsRequired().HasMaxLength(100).UseCollation("NOCASE");
        b.HasIndex(t => t.Name).IsUnique();
        b.HasData(SeedData.Teams);
    }
}

public class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> b)
    {
        b.HasKey(m => new { m.EmployeeId, m.TeamId });
        b.Property(m => m.Role).HasMaxLength(50);
        // Membership rows have no meaning without both sides, so they cascade.
        b.HasOne(m => m.Employee).WithMany(e => e.Memberships).HasForeignKey(m => m.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(m => m.Team).WithMany(t => t.Members).HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(m => m.TeamId);
        b.HasData(SeedData.Members);
    }
}

public class TopicConfiguration : IEntityTypeConfiguration<Topic>
{
    public void Configure(EntityTypeBuilder<Topic> b)
    {
        b.Property(t => t.Name).IsRequired().HasMaxLength(100).UseCollation("NOCASE");
        b.Property(t => t.Description).HasMaxLength(500);
        b.HasIndex(t => t.Name).IsUnique();
        b.HasData(SeedData.Topics);
    }
}

public class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> b)
    {
        b.Property(n => n.Title).IsRequired().HasMaxLength(200);
        b.Property(n => n.Content).IsRequired().HasMaxLength(20000);
        b.Property(n => n.Visibility).HasConversion<string>().HasMaxLength(10);
        b.Property(n => n.Version).IsConcurrencyToken();

        // Employees / teams that own notes cannot be deleted out from under them.
        b.HasOne(n => n.CreatedBy).WithMany().HasForeignKey(n => n.CreatedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(n => n.UpdatedBy).WithMany().HasForeignKey(n => n.UpdatedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(n => n.Team).WithMany().HasForeignKey(n => n.TeamId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(n => n.TeamId);
        b.HasIndex(n => n.CreatedByEmployeeId);
        b.HasIndex(n => n.UpdatedAt);

        // TeamId is set exactly when Visibility is Team.
        b.ToTable(t => t.HasCheckConstraint("CK_Notes_TeamId_Visibility",
            "(\"Visibility\" = 'Team' AND \"TeamId\" IS NOT NULL) OR (\"Visibility\" <> 'Team' AND \"TeamId\" IS NULL)"));

        b.HasData(SeedData.Notes);
    }
}

public class NoteTopicConfiguration : IEntityTypeConfiguration<NoteTopic>
{
    public void Configure(EntityTypeBuilder<NoteTopic> b)
    {
        b.HasKey(nt => new { nt.NoteId, nt.TopicId });
        // Deleting a note or a topic removes only the link rows, never the other side.
        b.HasOne(nt => nt.Note).WithMany(n => n.NoteTopics).HasForeignKey(nt => nt.NoteId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(nt => nt.Topic).WithMany().HasForeignKey(nt => nt.TopicId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(nt => nt.TopicId);
        b.HasData(SeedData.NoteTopics);
    }
}
