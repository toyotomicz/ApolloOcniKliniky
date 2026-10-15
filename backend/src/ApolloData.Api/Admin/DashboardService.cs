using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using ApolloData.Api.Plans;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Admin;

/// <summary>
/// Dashboard metrics. All values are computed from persisted data at request time.
/// "Today" and the daily buckets use the reporting time zone (REPORTING_TIME_ZONE, default Europe/Prague);
/// timestamps themselves are stored in UTC.
/// </summary>
/// <param name="TotalClinics">All clinic registrations ever stored, regardless of status.</param>
/// <param name="NewToday">Registrations whose <c>CreatedAt</c> falls on the current local day.</param>
/// <param name="PendingReview">Clinics currently in status Pending (awaiting administrator decision).</param>
/// <param name="Approved">Clinics currently in status Approved.</param>
/// <param name="Rejected">Clinics currently in status Rejected.</param>
/// <param name="UniqueClinicSignInsToday">Distinct clinics that completed a one-time-code sign-in today (from the activity log).</param>
/// <param name="EmailUnverified">Clinics that have not yet verified their email address.</param>
/// <param name="UnreadMessages">Messages from clinics that no administrator has opened yet.</param>
/// <param name="ApprovalRatePercent">Share of approved among currently decided registrations (approved + rejected); null without decisions.</param>
/// <param name="AverageDecisionHours">Mean time from registration to its first approve/reject decision, over decisions in the last 90 days.</param>
public sealed record DashboardMetrics(
    int TotalClinics,
    int NewToday,
    int PendingReview,
    int Approved,
    int Rejected,
    int UniqueClinicSignInsToday,
    int EmailUnverified,
    int UnreadMessages,
    int? ApprovalRatePercent,
    double? AverageDecisionHours);

public sealed record DailyCount(DateOnly Date, int Count);

/// <summary>Registration funnel: every registration, then verified email, then approved.</summary>
public sealed record FunnelDto(int Registered, int EmailVerified, int Approved);

public sealed record PlanCountDto(string PlanCode, string PlanName, int Count);

public sealed record ActivityItemDto(
    long Id, ActivityEventType Type, DateTime OccurredAt, Guid? ClinicId, string? ClinicName, string? AdministratorUsername, string? Detail);

public sealed record DashboardDto(
    DashboardMetrics Metrics,
    FunnelDto Funnel,
    IReadOnlyList<PlanCountDto> Plans,
    int SeriesDays,
    IReadOnlyList<DailyCount> Registrations,
    IReadOnlyList<ActivityItemDto> RecentActivity,
    string TimeZone,
    DateTime GeneratedAt);

public sealed class ReportingClock(TimeProvider time, IConfiguration configuration)
{
    public TimeZoneInfo TimeZone { get; } =
        TimeZoneInfo.FindSystemTimeZoneById(configuration["REPORTING_TIME_ZONE"] is { Length: > 0 } tz ? tz : "Europe/Prague");

    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(UtcNow, TimeZone));

    /// <summary>UTC instant at which the given local day starts.</summary>
    public DateTime StartOfDayUtc(DateOnly localDay) =>
        TimeZoneInfo.ConvertTimeToUtc(localDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), TimeZone);
}

public sealed class DashboardService(AppDbContext db, ReportingClock clock)
{
    public static readonly int[] AllowedSeriesDays = [7, 30];
    public const int DecisionWindowDays = 90;

    private static readonly ActivityEventType[] FeedTypes =
    [
        ActivityEventType.ClinicRegistered,
        ActivityEventType.ClinicEmailVerified,
        ActivityEventType.ClinicProfileUpdated,
        ActivityEventType.ClinicStatusChanged,
        ActivityEventType.ClinicMessageSent,
        ActivityEventType.AdminMessageSent,
        ActivityEventType.AdminNoteAdded,
    ];

    public async Task<DashboardDto> GetAsync(int seriesDays, CancellationToken ct)
    {
        if (!AllowedSeriesDays.Contains(seriesDays)) seriesDays = AllowedSeriesDays[0];
        var today = clock.Today;
        var startOfToday = clock.StartOfDayUtc(today);
        var firstChartDay = today.AddDays(-(seriesDays - 1));
        var chartStart = clock.StartOfDayUtc(firstChartDay);

        var byStatus = await db.Clinics
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        var newToday = await db.Clinics.CountAsync(c => c.CreatedAt >= startOfToday, ct);
        var unverified = await db.Clinics.CountAsync(c => c.EmailVerifiedAt == null, ct);
        var uniqueSignIns = await db.ActivityEvents
            .Where(e => e.Type == ActivityEventType.ClinicSignedIn && e.OccurredAt >= startOfToday && e.ClinicId != null)
            .Select(e => e.ClinicId)
            .Distinct()
            .CountAsync(ct);

        var verified = await db.Clinics.CountAsync(c => c.EmailVerifiedAt != null, ct);
        var unreadMessages = await db.ClinicMessages.CountAsync(m => m.Sender == MessageSender.Clinic && m.ReadAt == null, ct);

        var byPlan = await db.Clinics
            .GroupBy(c => c.PlanCode)
            .Select(g => new { Plan = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Plan, x => x.Count, ct);
        var plans = PlanCatalog.All
            .Select(p => new PlanCountDto(p.Code, p.Name, byPlan.GetValueOrDefault(p.Code)))
            .Concat(byPlan.Where(kv => PlanCatalog.Find(kv.Key) is null).Select(kv => new PlanCountDto(kv.Key, kv.Key, kv.Value)))
            .ToList();

        // First approve/reject decision per clinic, for decisions made in the last 90 days.
        var decisionWindowStart = clock.UtcNow.AddDays(-DecisionWindowDays);
        var firstDecisions = await db.ClinicStatusChanges
            .Where(s => s.ToStatus != ClinicStatus.Pending)
            .GroupBy(s => s.ClinicId)
            .Select(g => new { ClinicId = g.Key, DecidedAt = g.Min(s => s.ChangedAt) })
            .Where(d => d.DecidedAt >= decisionWindowStart)
            .Join(db.Clinics, d => d.ClinicId, c => c.Id, (d, c) => new { d.DecidedAt, c.CreatedAt })
            .ToListAsync(ct);
        double? averageDecisionHours = firstDecisions.Count == 0
            ? null
            : Math.Round(firstDecisions.Average(d => (d.DecidedAt - d.CreatedAt).TotalHours), 1);

        // Bounded to the chart window; bucketing happens in .NET so DST-aware local days are exact.
        var recentTimestamps = await db.Clinics
            .Where(c => c.CreatedAt >= chartStart)
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);

        var activity = await db.ActivityEvents
            .AsNoTracking()
            .Where(e => FeedTypes.Contains(e.Type))
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Take(12)
            .Select(e => new ActivityItemDto(e.Id, e.Type, e.OccurredAt, e.ClinicId,
                e.Clinic != null ? e.Clinic.Name : null,
                e.Administrator != null ? e.Administrator.Username : null,
                e.Detail))
            .ToListAsync(ct);

        var approved = byStatus.GetValueOrDefault(ClinicStatus.Approved);
        var rejected = byStatus.GetValueOrDefault(ClinicStatus.Rejected);
        var total = byStatus.Values.Sum();
        var metrics = new DashboardMetrics(
            TotalClinics: total,
            NewToday: newToday,
            PendingReview: byStatus.GetValueOrDefault(ClinicStatus.Pending),
            Approved: approved,
            Rejected: rejected,
            UniqueClinicSignInsToday: uniqueSignIns,
            EmailUnverified: unverified,
            UnreadMessages: unreadMessages,
            ApprovalRatePercent: approved + rejected == 0 ? null : (int)Math.Round(100.0 * approved / (approved + rejected)),
            AverageDecisionHours: averageDecisionHours);

        return new DashboardDto(
            metrics,
            new FunnelDto(total, verified, approved),
            plans,
            seriesDays,
            BuildDailySeries(recentTimestamps, today, clock.TimeZone, seriesDays),
            activity,
            clock.TimeZone.Id,
            clock.UtcNow);
    }

    /// <summary>Counts UTC timestamps per local calendar day for the <paramref name="days"/> days ending today; missing days are zero.</summary>
    public static IReadOnlyList<DailyCount> BuildDailySeries(IEnumerable<DateTime> utcTimestamps, DateOnly today, TimeZoneInfo timeZone, int days)
    {
        var counts = utcTimestamps
            .Select(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t, DateTimeKind.Utc), timeZone)))
            .GroupBy(d => d)
            .ToDictionary(g => g.Key, g => g.Count());

        return Enumerable.Range(0, days)
            .Select(offset => today.AddDays(offset - (days - 1)))
            .Select(day => new DailyCount(day, counts.GetValueOrDefault(day)))
            .ToList();
    }
}
