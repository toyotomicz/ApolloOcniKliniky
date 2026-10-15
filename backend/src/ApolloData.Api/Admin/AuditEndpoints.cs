using System.Globalization;
using System.Security.Claims;
using System.Text;
using ApolloData.Api.Auth;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Admin;

public enum AuditActor
{
    Clinic,
    Administrator,
    System,
}

public sealed record AuditQuery(
    [FromQuery] DateOnly? From,
    [FromQuery] DateOnly? To,
    [FromQuery] string? Type,
    [FromQuery] string? Actor,
    [FromQuery] Guid? ClinicId,
    [FromQuery] string? Search,
    [FromQuery] int? Page,
    [FromQuery] int? PageSize);

public sealed record AuditItemDto(
    long Id, DateTime OccurredAt, ActivityEventType Type, AuditActor Actor, string? ActorName,
    Guid? ClinicId, string? ClinicName, string? Detail);

/// <summary>
/// Searchable, downloadable audit log over <see cref="ActivityEvent"/>. Who performed an event follows from
/// its type: clinic self-service events, system events (email delivery, maintenance) and everything else by an administrator.
/// </summary>
public static class AuditEndpoints
{
    public const int MaxExportRows = 100_000;

    public static readonly ActivityEventType[] ClinicEvents =
    [
        ActivityEventType.ClinicRegistered, ActivityEventType.ClinicEmailVerified, ActivityEventType.ClinicSignedIn,
        ActivityEventType.ClinicSignedOut, ActivityEventType.ClinicProfileUpdated, ActivityEventType.ClinicMessageSent,
    ];

    public static readonly ActivityEventType[] SystemEvents =
    [
        ActivityEventType.EmailSent, ActivityEventType.EmailFailed, ActivityEventType.AdminPasswordReset,
        ActivityEventType.DemoDataSeeded, ActivityEventType.DemoDataRemoved,
    ];

    /// <summary>Czech labels used in the CSV export (the SPA has its own copy for display).</summary>
    public static readonly IReadOnlyDictionary<ActivityEventType, string> Labels = new Dictionary<ActivityEventType, string>
    {
        [ActivityEventType.ClinicRegistered] = "Registrace kliniky",
        [ActivityEventType.ClinicEmailVerified] = "Ověření e-mailu",
        [ActivityEventType.ClinicSignedIn] = "Přihlášení kliniky",
        [ActivityEventType.ClinicSignedOut] = "Odhlášení kliniky",
        [ActivityEventType.ClinicProfileUpdated] = "Úprava údajů kliniky",
        [ActivityEventType.ClinicStatusChanged] = "Změna stavu registrace",
        [ActivityEventType.ClinicMessageSent] = "Zpráva od kliniky",
        [ActivityEventType.ConfirmationDownloaded] = "Stažení potvrzení (PDF)",
        [ActivityEventType.AdminNoteAdded] = "Interní poznámka",
        [ActivityEventType.AdminSignedIn] = "Přihlášení administrátora",
        [ActivityEventType.AdminSignedOut] = "Odhlášení administrátora",
        [ActivityEventType.AdminSignInFailed] = "Neúspěšné přihlášení administrátora",
        [ActivityEventType.AdminMessageSent] = "Zpráva od administrátora",
        [ActivityEventType.AdminTwoFactorEnabled] = "Zapnutí dvoufázového ověření",
        [ActivityEventType.AdminTwoFactorDisabled] = "Vypnutí dvoufázového ověření",
        [ActivityEventType.AdminRecoveryCodesRenewed] = "Nové záložní kódy",
        [ActivityEventType.AdminPasswordReset] = "Reset hesla administrátora",
        [ActivityEventType.AuditLogExported] = "Export audit logu",
        [ActivityEventType.EmailSent] = "Odeslaný e-mail",
        [ActivityEventType.EmailFailed] = "Neodeslaný e-mail",
        [ActivityEventType.DemoDataSeeded] = "Nahrání demo dat",
        [ActivityEventType.DemoDataRemoved] = "Odstranění demo dat",
    };

    public static void MapAuditEndpoints(this RouteGroupBuilder adminGroup)
    {
        adminGroup.MapGet("/audit", ListAsync);
        adminGroup.MapGet("/audit/export", ExportAsync);
    }

    public static AuditActor ActorOf(ActivityEventType type, Guid? administratorId) =>
        SystemEvents.Contains(type) ? AuditActor.System
        : administratorId is not null || !ClinicEvents.Contains(type) ? AuditActor.Administrator
        : AuditActor.Clinic;

    private static async Task<IResult> ListAsync([AsParameters] AuditQuery query, AppDbContext db, ReportingClock clock, CancellationToken ct)
    {
        var (events, errors) = Filter(db, query, clock);
        if (events is null) return ApiProblems.Validation(errors);

        var page = Math.Max(1, query.Page ?? 1);
        var pageSize = Math.Clamp(query.PageSize ?? 50, 1, 200);
        var total = await events.CountAsync(ct);
        var items = await LoadAsync(events.Skip((page - 1) * pageSize).Take(pageSize), ct);
        return TypedResults.Ok(new PagedResult<AuditItemDto>(items, page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
    }

    private static async Task<IResult> ExportAsync(
        [AsParameters] AuditQuery query, ClaimsPrincipal user, AppDbContext db, ReportingClock clock, CancellationToken ct)
    {
        var (events, errors) = Filter(db, query, clock);
        if (events is null) return ApiProblems.Validation(errors);

        var rows = await LoadAsync(events.Take(MaxExportRows), ct);

        var csv = new StringBuilder();
        csv.AppendLine("Čas;Událost;Kdo;Jméno;Klinika;ID kliniky;Detail");
        foreach (var r in rows)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.OccurredAt, DateTimeKind.Utc), clock.TimeZone);
            csv.AppendJoin(';', [
                Cell(local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Cell(Labels.GetValueOrDefault(r.Type, r.Type.ToString())),
                Cell(r.Actor switch { AuditActor.Clinic => "Klinika", AuditActor.Administrator => "Administrátor", _ => "Systém" }),
                Cell(r.ActorName),
                Cell(r.ClinicName),
                Cell(r.ClinicId?.ToString()),
                Cell(DescribeDetail(r.Type, r.Detail)),
            ]).Append("\r\n");
        }

        var now = clock.UtcNow;
        db.Record(ActivityEventType.AuditLogExported, now, administratorId: user.GetSubjectId(), detail: $"rows={rows.Count}");
        await db.SaveChangesAsync(ct);

        // UTF-8 with BOM and semicolons: opens correctly (with Czech diacritics) in Excel with Czech regional settings.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        var fileName = $"audit-log-{TimeZoneInfo.ConvertTimeFromUtc(now, clock.TimeZone):yyyyMMdd-HHmm}.csv";
        return TypedResults.File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static readonly IReadOnlyDictionary<string, string> StatusLabels = new Dictionary<string, string>
    {
        [nameof(ClinicStatus.Pending)] = "Čeká na posouzení",
        [nameof(ClinicStatus.Approved)] = "Schváleno",
        [nameof(ClinicStatus.Rejected)] = "Zamítnuto",
    };

    /// <summary>Human-readable detail for the export, e.g. "Pending→Approved;bulk" → "Čeká na posouzení → Schváleno (hromadně)".</summary>
    public static string? DescribeDetail(ActivityEventType type, string? detail)
    {
        if (detail is null || type != ActivityEventType.ClinicStatusChanged) return detail;
        var parts = detail.Split(';');
        var states = parts[0].Split('→');
        if (states.Length != 2) return detail;
        var text = $"{StatusLabels.GetValueOrDefault(states[0], states[0])} → {StatusLabels.GetValueOrDefault(states[1], states[1])}";
        return parts.Contains("bulk") ? $"{text} (hromadně)" : text;
    }

    /// <summary>
    /// Quotes a CSV cell and neutralises spreadsheet formulas (CSV injection): values starting with = + - @ or a
    /// control character get a leading apostrophe, so e.g. a clinic named "=HYPERLINK(...)" stays plain text.
    /// </summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static (IQueryable<ActivityEvent>? Events, ValidationErrors Errors) Filter(AppDbContext db, AuditQuery query, ReportingClock clock)
    {
        var errors = new ValidationErrors();
        ActivityEventType? type = null;
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            if (Enum.TryParse<ActivityEventType>(query.Type, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) type = parsed;
            else errors.Add("type", "Neznámý typ události.");
        }
        AuditActor? actor = null;
        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            if (Enum.TryParse<AuditActor>(query.Actor, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) actor = parsed;
            else errors.Add("actor", "Neznámý typ aktéra.");
        }
        if (query.From is { } f && query.To is { } t && f > t) errors.Add("to", "Konec období musí být po jeho začátku.");
        var search = query.Search?.Trim();
        if (search is { Length: > 100 }) errors.Add("search", "Hledaný výraz je příliš dlouhý.");
        if (!errors.IsValid) return (null, errors);

        IQueryable<ActivityEvent> events = db.ActivityEvents.AsNoTracking();
        if (query.From is { } from)
        {
            var fromUtc = clock.StartOfDayUtc(from);
            events = events.Where(e => e.OccurredAt >= fromUtc);
        }
        if (query.To is { } to)
        {
            var toUtc = clock.StartOfDayUtc(to.AddDays(1));
            events = events.Where(e => e.OccurredAt < toUtc);
        }
        if (type is not null) events = events.Where(e => e.Type == type);
        if (query.ClinicId is { } clinicId) events = events.Where(e => e.ClinicId == clinicId);
        events = actor switch
        {
            AuditActor.System => events.Where(e => SystemEvents.Contains(e.Type)),
            AuditActor.Clinic => events.Where(e => !SystemEvents.Contains(e.Type) && e.AdministratorId == null && ClinicEvents.Contains(e.Type)),
            AuditActor.Administrator => events.Where(e => !SystemEvents.Contains(e.Type) && (e.AdministratorId != null || !ClinicEvents.Contains(e.Type))),
            _ => events,
        };
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{AdminEndpoints.EscapeLike(search)}%";
            events = events.Where(e =>
                (e.Clinic != null && EF.Functions.ILike(e.Clinic.Name, pattern))
                || (e.Administrator != null && EF.Functions.ILike(e.Administrator.Username, pattern))
                || (e.Detail != null && EF.Functions.ILike(e.Detail, pattern)));
        }
        return (events.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id), errors);
    }

    private static async Task<List<AuditItemDto>> LoadAsync(IQueryable<ActivityEvent> events, CancellationToken ct)
    {
        var rows = await events
            .Select(e => new
            {
                e.Id, e.OccurredAt, e.Type, e.AdministratorId, e.ClinicId, e.Detail,
                AdminName = e.Administrator != null ? e.Administrator.Username : null,
                ClinicName = e.Clinic != null ? e.Clinic.Name : null,
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var actor = ActorOf(r.Type, r.AdministratorId);
            var actorName = actor switch
            {
                AuditActor.Administrator => r.AdminName,
                AuditActor.Clinic => r.ClinicName,
                _ => null,
            };
            return new AuditItemDto(r.Id, r.OccurredAt, r.Type, actor, actorName, r.ClinicId, r.ClinicName, r.Detail);
        }).ToList();
    }
}
