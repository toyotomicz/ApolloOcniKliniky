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
        b.Property(c => c.Website).HasMaxLength(300);
        b.Property(c => c.PatientInfo).HasMaxLength(1000);

        // PostgreSQL's xmin changes on every row update and serves as an optimistic concurrency token.
        b.Property(c => c.Version).IsRowVersion();

        b.HasIndex(c => c.NormalizedEmail).IsUnique();
        b.HasIndex(c => c.Status);
        b.HasIndex(c => c.PlanCode);
        b.HasIndex(c => c.CreatedAt);
        b.HasIndex(c => c.OrganizationId);
        b.HasIndex(c => c.IsDemo);

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
        b.HasMany(c => c.Messages).WithOne().HasForeignKey(m => m.ClinicId).OnDelete(DeleteBehavior.Cascade);
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

internal sealed class RegistrationEmailVerificationConfiguration : IEntityTypeConfiguration<RegistrationEmailVerification>
{
    public void Configure(EntityTypeBuilder<RegistrationEmailVerification> b)
    {
        b.HasKey(v => v.Id);
        b.Property(v => v.NormalizedEmail).HasMaxLength(254);
        b.Property(v => v.CodeHash).HasMaxLength(32);
        b.Property(v => v.TokenHash).HasMaxLength(32);
        b.HasIndex(v => new { v.NormalizedEmail, v.CreatedAt });
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
        b.Property(a => a.TwoFactorSecret).HasMaxLength(1024);
        b.Property(a => a.TwoFactorPendingSecret).HasMaxLength(1024);
        b.HasIndex(a => a.NormalizedUsername).IsUnique();
    }
}

internal sealed class AdminRecoveryCodeConfiguration : IEntityTypeConfiguration<AdminRecoveryCode>
{
    public void Configure(EntityTypeBuilder<AdminRecoveryCode> b)
    {
        b.HasKey(c => c.Id);
        b.Property(c => c.CodeHash).HasMaxLength(32);
        b.HasOne<Administrator>().WithMany().HasForeignKey(c => c.AdministratorId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(c => c.AdministratorId);
    }
}

internal sealed class ClinicMessageConfiguration : IEntityTypeConfiguration<ClinicMessage>
{
    public void Configure(EntityTypeBuilder<ClinicMessage> b)
    {
        b.HasKey(m => m.Id);
        b.Property(m => m.Body).HasMaxLength(4000);
        b.HasOne(m => m.Administrator).WithMany().HasForeignKey(m => m.AdministratorId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(m => new { m.ClinicId, m.CreatedAt });
        // Unread counters: "messages from clinics nobody has read yet" and per-clinic unread counts.
        b.HasIndex(m => new { m.Sender, m.ReadAt });
        b.ToTable(t => t.HasCheckConstraint(
            "CK_ClinicMessages_Author",
            """("Sender" = 'Clinic' AND "AdministratorId" IS NULL) OR ("Sender" = 'Administrator' AND "AdministratorId" IS NOT NULL)"""));
    }
}

internal sealed class OutboundEmailConfiguration : IEntityTypeConfiguration<OutboundEmail>
{
    public void Configure(EntityTypeBuilder<OutboundEmail> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.ToEmail).HasMaxLength(254);
        b.Property(e => e.ToName).HasMaxLength(200);
        b.Property(e => e.Subject).HasMaxLength(300);
        b.Property(e => e.LastError).HasMaxLength(500);
        b.HasOne<Clinic>().WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.SetNull);
        // The worker only ever looks at undelivered rows that are due.
        b.HasIndex(e => e.NextAttemptAt).HasFilter("\"SentAt\" IS NULL AND \"FailedAt\" IS NULL");
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
        b.Property(e => e.Detail).HasMaxLength(500);
        b.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(e => e.Administrator).WithMany().HasForeignKey(e => e.AdministratorId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(e => e.OccurredAt);
        b.HasIndex(e => new { e.Type, e.OccurredAt });
        b.HasIndex(e => new { e.ClinicId, e.OccurredAt });
    }
}
