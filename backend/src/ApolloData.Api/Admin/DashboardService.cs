using ApolloData.Api.Data;
using ApolloData.Api.Domain;
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
public sealed record DashboardMetrics(
    int TotalClinics,
    int NewToday,
    int PendingReview,
    int Approved,
    int Rejected,
    int UniqueClinicSignInsToday,
    int EmailUnverified);

public sealed record DailyCount(DateOnly Date, int Count);

public sealed record ActivityItemDto(
    long Id, ActivityEventType Type, DateTime OccurredAt, Guid? ClinicId, string? ClinicName, string? AdministratorUsername, string? Detail);

public sealed record DashboardDto(
    DashboardMetrics Metrics,
    IReadOnlyList<DailyCount> RegistrationsLast7Days,
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
    public const int ChartDays = 7;

    private static readonly ActivityEventType[] FeedTypes =
    [
        ActivityEventType.ClinicRegistered,
        ActivityEventType.ClinicEmailVerified,
        ActivityEventType.ClinicProfileUpdated,
        ActivityEventType.ClinicStatusChanged,
        ActivityEventType.AdminNoteAdded,
    ];

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var startOfToday = clock.StartOfDayUtc(today);
        var firstChartDay = today.AddDays(-(ChartDays - 1));
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

        // Bounded to 7 days of registrations; bucketing happens in .NET so DST-aware local days are exact.
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

        var metrics = new DashboardMetrics(
            TotalClinics: byStatus.Values.Sum(),
            NewToday: newToday,
            PendingReview: byStatus.GetValueOrDefault(ClinicStatus.Pending),
            Approved: byStatus.GetValueOrDefault(ClinicStatus.Approved),
            Rejected: byStatus.GetValueOrDefault(ClinicStatus.Rejected),
            UniqueClinicSignInsToday: uniqueSignIns,
            EmailUnverified: unverified);

        return new DashboardDto(
            metrics,
            BuildDailySeries(recentTimestamps, today, clock.TimeZone, ChartDays),
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
