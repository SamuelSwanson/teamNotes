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
        b.Property(e => e.PasswordHash).HasMaxLength(200);
        b.Property(e => e.Role).HasConversion<string>().HasMaxLength(10).HasDefaultValue(EmployeeRole.Employee);
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

public class NoteRevisionConfiguration : IEntityTypeConfiguration<NoteRevision>
{
    public void Configure(EntityTypeBuilder<NoteRevision> b)
    {
        b.HasKey(r => r.RevisionId);
        b.Property(r => r.Action).IsRequired().HasMaxLength(10);
        b.Property(r => r.Title).IsRequired().HasMaxLength(200);
        b.Property(r => r.Content).IsRequired().HasMaxLength(20000);
        b.Property(r => r.Visibility).HasConversion<string>().HasMaxLength(10);
        b.Property(r => r.TeamName).HasMaxLength(100);
        b.Property(r => r.Topics).IsRequired().HasMaxLength(2000);
        // History belongs to the note: it is removed with the note. Editors cannot be deleted while history references them.
        b.HasOne(r => r.Note).WithMany(n => n.Revisions).HasForeignKey(r => r.NoteId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(r => r.EditedBy).WithMany().HasForeignKey(r => r.EditedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => r.NoteId);
        b.HasIndex(r => r.EditedByEmployeeId);
        b.HasData(SeedData.Revisions);
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

public class NoteAttachmentConfiguration : IEntityTypeConfiguration<NoteAttachment>
{
    public void Configure(EntityTypeBuilder<NoteAttachment> b)
    {
        b.HasKey(a => a.AttachmentId);
        b.Property(a => a.FileName).IsRequired().HasMaxLength(150);
        b.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
        b.Property(a => a.StoredName).IsRequired().HasMaxLength(64);
        b.HasIndex(a => a.StoredName).IsUnique();
        // Attachment rows go with their note (the service also removes the files). Uploaders cannot be deleted while referenced.
        b.HasOne(a => a.Note).WithMany(n => n.Attachments).HasForeignKey(a => a.NoteId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(a => a.UploadedBy).WithMany().HasForeignKey(a => a.UploadedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(a => a.NoteId);
        b.HasIndex(a => a.UploadedByEmployeeId);
    }
}
