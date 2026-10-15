using ApolloData.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ApolloData.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<ClinicLoginChallenge> ClinicLoginChallenges => Set<ClinicLoginChallenge>();
    public DbSet<Administrator> Administrators => Set<Administrator>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<ClinicStatusChange> ClinicStatusChanges => Set<ClinicStatusChange>();
    public DbSet<AdminNote> AdminNotes => Set<AdminNote>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<ClinicMessage> ClinicMessages => Set<ClinicMessage>();
    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();
    public DbSet<AdminRecoveryCode> AdminRecoveryCodes => Set<AdminRecoveryCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as readable strings rather than integers.
        configurationBuilder.Properties<ClinicStatus>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<SessionKind>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<ActivityEventType>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<OnboardingPreference>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<MessageSender>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<OutboundEmailKind>().HaveConversion<string>().HaveMaxLength(32);
    }
}

/// <summary>Used by <c>dotnet ef</c> so migrations can be created without running the application host.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=apollodata_design;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        return new AppDbContext(options);
    }
}
