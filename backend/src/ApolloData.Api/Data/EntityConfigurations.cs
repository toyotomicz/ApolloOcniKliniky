using ApolloData.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApolloData.Api.Data;

internal sealed class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
{
    public void Configure(EntityTypeBuilder<Clinic> b)
    {
        b.HasKey(c => c.Id);
        b.Property(c => c.Name).HasMaxLength(200);
        b.Property(c => c.OrganizationId).HasMaxLength(8);
        b.Property(c => c.ContactFirstName).HasMaxLength(100);
        b.Property(c => c.ContactLastName).HasMaxLength(100);
        b.Property(c => c.Email).HasMaxLength(254);
        b.Property(c => c.NormalizedEmail).HasMaxLength(254);
        b.Property(c => c.Phone).HasMaxLength(20);
        b.Property(c => c.Street).HasMaxLength(200);
        b.Property(c => c.City).HasMaxLength(100);
        b.Property(c => c.PostalCode).HasMaxLength(6);
        b.Property(c => c.PlanCode).HasMaxLength(32);
        b.Property(c => c.StatusMessage).HasMaxLength(1000);
        b.Property(c => c.PrivacyNoticeVersion).HasMaxLength(32);

        // PostgreSQL's xmin changes on every row update and serves as an optimistic concurrency token.
        b.Property(c => c.Version).IsRowVersion();

        b.HasIndex(c => c.NormalizedEmail).IsUnique();
        b.HasIndex(c => c.Status);
        b.HasIndex(c => c.PlanCode);
        b.HasIndex(c => c.CreatedAt);
        b.HasIndex(c => c.OrganizationId);

        b.OwnsOne(c => c.OperationalContact, o =>
        {
            o.Property(x => x.Name).HasColumnName("OperationalContactName").HasMaxLength(200);
            o.Property(x => x.Email).HasColumnName("OperationalContactEmail").HasMaxLength(254);
            o.Property(x => x.Phone).HasColumnName("OperationalContactPhone").HasMaxLength(20);
        });

        b.OwnsOne(c => c.ServiceConfiguration, o =>
        {
            o.Property(x => x.WorkplaceCount).HasColumnName("ServiceWorkplaceCount");
            o.Property(x => x.OnboardingPreference).HasColumnName("ServiceOnboardingPreference");
            o.Property(x => x.TechnicalContactEmail).HasColumnName("ServiceTechnicalContactEmail").HasMaxLength(254);
            o.Property(x => x.Notes).HasColumnName("ServiceNotes").HasMaxLength(1000);
        });

        b.HasMany(c => c.StatusChanges).WithOne().HasForeignKey(s => s.ClinicId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(c => c.Notes).WithOne().HasForeignKey(n => n.ClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ClinicLoginChallengeConfiguration : IEntityTypeConfiguration<ClinicLoginChallenge>
{
    public void Configure(EntityTypeBuilder<ClinicLoginChallenge> b)
    {
        b.HasKey(c => c.Id);
        b.Property(c => c.CodeHash).HasMaxLength(32);
        b.HasOne(c => c.Clinic).WithMany().HasForeignKey(c => c.ClinicId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(c => new { c.ClinicId, c.CreatedAt });
    }
}

internal sealed class AdministratorConfiguration : IEntityTypeConfiguration<Administrator>
{
    public void Configure(EntityTypeBuilder<Administrator> b)
    {
        b.HasKey(a => a.Id);
        b.Property(a => a.Username).HasMaxLength(64);
        b.Property(a => a.NormalizedUsername).HasMaxLength(64);
        b.Property(a => a.PasswordHash).HasMaxLength(512);
        b.HasIndex(a => a.NormalizedUsername).IsUnique();
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> b)
    {
        b.HasKey(s => s.Id);
        b.HasOne<Clinic>().WithMany().HasForeignKey(s => s.ClinicId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Administrator>().WithMany().HasForeignKey(s => s.AdministratorId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(s => s.ClinicId);
        b.HasIndex(s => s.AdministratorId);
        b.ToTable(t => t.HasCheckConstraint(
            "CK_UserSessions_Owner",
            """("Kind" = 'Clinic' AND "ClinicId" IS NOT NULL AND "AdministratorId" IS NULL) OR ("Kind" = 'Administrator' AND "AdministratorId" IS NOT NULL AND "ClinicId" IS NULL)"""));
    }
}

internal sealed class ClinicStatusChangeConfiguration : IEntityTypeConfiguration<ClinicStatusChange>
{
    public void Configure(EntityTypeBuilder<ClinicStatusChange> b)
    {
        b.HasKey(s => s.Id);
        b.Property(s => s.Reason).HasMaxLength(1000);
        b.HasOne(s => s.Administrator).WithMany().HasForeignKey(s => s.AdministratorId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(s => new { s.ClinicId, s.ChangedAt });
    }
}

internal sealed class AdminNoteConfiguration : IEntityTypeConfiguration<AdminNote>
{
    public void Configure(EntityTypeBuilder<AdminNote> b)
    {
        b.HasKey(n => n.Id);
        b.Property(n => n.Text).HasMaxLength(2000);
        b.HasOne(n => n.Administrator).WithMany().HasForeignKey(n => n.AdministratorId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(n => new { n.ClinicId, n.CreatedAt });
    }
}

internal sealed class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).UseIdentityAlwaysColumn();
        b.Property(e => e.Detail).HasMaxLength(200);
        b.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(e => e.Administrator).WithMany().HasForeignKey(e => e.AdministratorId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(e => e.OccurredAt);
        b.HasIndex(e => new { e.Type, e.OccurredAt });
    }
}
