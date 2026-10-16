using System.Security.Claims;
using ApolloData.Api.Auth;
using ApolloData.Api.Clinics;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Documents;
using ApolloData.Api.Domain;
using ApolloData.Api.Email;
using ApolloData.Api.Messages;
using ApolloData.Api.Plans;
using ApolloData.Api.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Admin;

public sealed record ClinicListQuery(
    [FromQuery] string? Search,
    [FromQuery] string? Status,
    [FromQuery] string? Plan,
    [FromQuery] bool? Unread,
    [FromQuery] string? Sort,
    [FromQuery] string? Direction,
    [FromQuery] int? Page,
    [FromQuery] int? PageSize);

public sealed record ClinicListItemDto(
    Guid Id, string Name, string Email, string? OrganizationId, string City, string PlanCode,
    ClinicStatus Status, bool EmailVerified, DateTime CreatedAt, int UnreadMessages);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record StatusChangeDto(Guid Id, ClinicStatus FromStatus, ClinicStatus ToStatus, DateTime ChangedAt, string? Reason, string AdministratorUsername);

public sealed record AdminNoteDto(Guid Id, string Text, DateTime CreatedAt, string AdministratorUsername);

public sealed record LocationDto(double Latitude, double Longitude);

public sealed record AdminClinicDetailDto(
    ClinicProfileDto Profile,
    uint Version,
    DateTime PrivacyNoticeAcceptedAt,
    string PrivacyNoticeVersion,
    DateTime? LastSignInAt,
    IReadOnlyList<ClinicStatus> AllowedStatusChanges,
    IReadOnlyList<StatusChangeDto> StatusHistory,
    IReadOnlyList<AdminNoteDto> Notes,
    LocationDto? Location,
    bool IsDemo);

public sealed record ChangeStatusRequest(ClinicStatus? Status, string? Message, string? InternalNote, uint? Version);

public sealed record AddNoteRequest(string? Text);

public sealed record BulkStatusRequest(IReadOnlyList<Guid>? ClinicIds, ClinicStatus? Status, string? Message);

/// <param name="Updated">Clinics whose status changed (each gets a history record, audit event and email).</param>
/// <param name="Skipped">Clinics that already had the requested status.</param>
/// <param name="NotFound">Ids that do not exist (e.g. deleted demo data).</param>
public sealed record BulkStatusResult(IReadOnlyList<Guid> Updated, IReadOnlyList<Guid> Skipped, IReadOnlyList<Guid> NotFound);

public sealed record BulkDeleteRequest(IReadOnlyList<Guid>? ClinicIds);

public sealed record BulkDeleteResult(IReadOnlyList<Guid> Deleted, IReadOnlyList<Guid> NotFound);

public sealed record MapClinicDto(Guid Id, string Name, string City, string PlanCode, ClinicStatus Status, double Latitude, double Longitude);

/// <param name="WithoutLocation">Matching clinics whose address has not been resolved (yet).</param>
public sealed record ClinicMapDto(IReadOnlyList<MapClinicDto> Items, int WithoutLocation);

public static class AdminEndpoints
{
    public const int MaxPageSize = 100;
    public const int MaxBulkSize = 100;

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization(AuthPolicies.Admin)
            .WithTags("Administration");

        group.MapGet("/dashboard", (DashboardService dashboard, [FromQuery] int? days, CancellationToken ct) =>
            dashboard.GetAsync(days ?? DashboardService.AllowedSeriesDays[0], ct));
        group.MapGet("/clinics", ListAsync);
        group.MapGet("/clinics/map", MapAsync);
        group.MapGet("/clinics/{id:guid}", DetailAsync);
        group.MapGet("/clinics/{id:guid}/confirmation.pdf", ConfirmationAsync);
        group.MapPost("/clinics/{id:guid}/status", ChangeStatusAsync);
        group.MapPost("/clinics/bulk-status", BulkStatusAsync);
        group.MapPost("/clinics/{id:guid}/notes", AddNoteAsync);
        group.MapDelete("/clinics/{id:guid}", DeleteAsync);
        group.MapPost("/clinics/bulk-delete", BulkDeleteAsync);
        group.MapAdminMessageEndpoints();
        group.MapAuditEndpoints();
    }

    private static (IQueryable<Clinic>? Clinics, ValidationErrors Errors) Filter(AppDbContext db, string? searchInput, string? statusInput, string? planInput, bool? unread)
    {
        var errors = new ValidationErrors();
        ClinicStatus? status = null;
        if (!string.IsNullOrWhiteSpace(statusInput))
        {
            if (Enum.TryParse<ClinicStatus>(statusInput, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) status = parsed;
            else errors.Add("status", "Neznámý stav.");
        }
        string? plan = null;
        if (!string.IsNullOrWhiteSpace(planInput))
        {
            plan = PlanCatalog.Find(planInput)?.Code;
            if (plan is null) errors.Add("plan", "Neznámý plán.");
        }
        var search = searchInput?.Trim();
        if (search is { Length: > 100 }) errors.Add("search", "Hledaný výraz je příliš dlouhý.");
        if (!errors.IsValid) return (null, errors);

        IQueryable<Clinic> clinics = db.Clinics.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            // Wildcards in user input are escaped; ILIKE runs as a parameterized query.
            var pattern = $"%{EscapeLike(search)}%";
            var compact = search.Replace(" ", "");
            clinics = clinics.Where(c =>
                EF.Functions.ILike(c.Name, pattern)
                || EF.Functions.ILike(c.NormalizedEmail, pattern)
                || EF.Functions.ILike(c.City, pattern)
                || (c.OrganizationId != null && c.OrganizationId.StartsWith(compact)));
        }
        if (status is not null) clinics = clinics.Where(c => c.Status == status);
        if (plan is not null) clinics = clinics.Where(c => c.PlanCode == plan);
        if (unread == true) clinics = clinics.Where(c => c.Messages.Any(m => m.Sender == MessageSender.Clinic && m.ReadAt == null));
        return (clinics, errors);
    }

    private static async Task<IResult> ListAsync([AsParameters] ClinicListQuery query, AppDbContext db, CancellationToken ct)
    {
        var (clinics, errors) = Filter(db, query.Search, query.Status, query.Plan, query.Unread);
        if (clinics is null) return ApiProblems.Validation(errors);

        var page = Math.Max(1, query.Page ?? 1);
        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, MaxPageSize);

        var descending = !string.Equals(query.Direction, "asc", StringComparison.OrdinalIgnoreCase);
        clinics = (query.Sort?.ToLowerInvariant(), descending) switch
        {
            ("name", false) => clinics.OrderBy(c => c.Name).ThenBy(c => c.Id),
            ("name", true) => clinics.OrderByDescending(c => c.Name).ThenBy(c => c.Id),
            ("status", false) => clinics.OrderBy(c => c.Status).ThenByDescending(c => c.CreatedAt),
            ("status", true) => clinics.OrderByDescending(c => c.Status).ThenByDescending(c => c.CreatedAt),
            ("plan", false) => clinics.OrderBy(c => c.PlanCode).ThenByDescending(c => c.CreatedAt),
            ("plan", true) => clinics.OrderByDescending(c => c.PlanCode).ThenByDescending(c => c.CreatedAt),
            (_, false) => clinics.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            _ => clinics.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id),
        };

        var total = await clinics.CountAsync(ct);
        var items = await clinics
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ClinicListItemDto(c.Id, c.Name, c.Email, c.OrganizationId, c.City, c.PlanCode,
                c.Status, c.EmailVerifiedAt != null, c.CreatedAt,
                c.Messages.Count(m => m.Sender == MessageSender.Clinic && m.ReadAt == null)))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<ClinicListItemDto>(items, page, pageSize, total,
            (int)Math.Ceiling(total / (double)pageSize)));
    }

    private static async Task<IResult> MapAsync([FromQuery] string? status, [FromQuery] string? plan, AppDbContext db, CancellationToken ct)
    {
        var (clinics, errors) = Filter(db, null, status, plan, null);
        if (clinics is null) return ApiProblems.Validation(errors);

        var items = await clinics
            .Where(c => c.Latitude != null && c.Longitude != null)
            .OrderBy(c => c.Name)
            .Select(c => new MapClinicDto(c.Id, c.Name, c.City, c.PlanCode, c.Status, c.Latitude!.Value, c.Longitude!.Value))
            .ToListAsync(ct);
        var withoutLocation = await clinics.CountAsync(c => c.Latitude == null || c.Longitude == null, ct);
        return TypedResults.Ok(new ClinicMapDto(items, withoutLocation));
    }

    private static async Task<IResult> DetailAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var detail = await LoadDetailAsync(db, id, ct);
        return detail is null ? ApiProblems.NotFound("Klinika nebyla nalezena.") : TypedResults.Ok(detail);
    }

    private static async Task<IResult> ConfirmationAsync(
        Guid id, ClaimsPrincipal user, AppDbContext db, AppInfo app, ReportingClock clock, CancellationToken ct)
    {
        var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (clinic is null) return ApiProblems.NotFound("Klinika nebyla nalezena.");

        db.Record(ActivityEventType.ConfirmationDownloaded, clock.UtcNow, clinic.Id, user.GetSubjectId());
        await db.SaveChangesAsync(ct);
        var pdf = ConfirmationPdf.Generate(clinic, app.Name, clock.UtcNow, clock.TimeZone);
        return TypedResults.File(pdf, "application/pdf", ConfirmationPdf.FileName(clinic));
    }

    private static async Task<IResult> ChangeStatusAsync(
        Guid id, ChangeStatusRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time,
        ClinicStatusChanger statusChanger, EmailOutboxSignal outbox, IAdminNotifier notifier,
        ILogger<ChangeStatusRequest> logger, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (request.Status is not { } target || !Enum.IsDefined(target)) errors.Add("status", "Vyberte nový stav.");
        if (request.Version is null) errors.Add("version", "Chybí verze záznamu.");
        var message = Rules.Optional(errors, "message", request.Message, 1000);
        var internalNote = Rules.Optional(errors, "internalNote", request.InternalNote, 2000);
        if (request.Status == ClinicStatus.Rejected && message is null && !errors.Has("message"))
        {
            errors.Add("message", "Uveďte důvod zamítnutí. Zobrazí se klinice v jejím portálu.");
        }
        if (!errors.IsValid) return ApiProblems.Validation(errors);

        var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (clinic is null) return ApiProblems.NotFound("Klinika nebyla nalezena.");

        var newStatus = request.Status!.Value;
        if (clinic.Status == newStatus)
        {
            return ApiProblems.Conflict("Klinika již tento stav má.", "status_unchanged");
        }

        var adminId = user.GetSubjectId();
        var now = time.GetUtcNow().UtcDateTime;
        var previous = clinic.Status;

        // Optimistic concurrency: the update only succeeds if xmin still matches the version the admin saw.
        db.Entry(clinic).Property(c => c.Version).OriginalValue = request.Version!.Value;

        statusChanger.Apply(clinic, newStatus, message, adminId, now);
        if (internalNote is not null)
        {
            db.AdminNotes.Add(new AdminNote
            {
                Id = Guid.CreateVersion7(), ClinicId = clinic.Id, AdministratorId = adminId, Text = internalNote, CreatedAt = now,
            });
        }

        // A single SaveChanges runs in one database transaction: status, history, note, audit event and the
        // queued notification email are committed together or not at all.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.Conflict(
                "Záznam mezitím upravil někdo jiný. Načtěte prosím aktuální údaje a akci opakujte.", "concurrency_conflict");
        }

        outbox.Notify();
        await notifier.ClinicsChangedAsync(new ClinicsChangedEvent([clinic.Id], "status"));
        logger.LogInformation("Clinic {ClinicId} status changed {From} -> {To} by administrator {AdminId}",
            clinic.Id, previous, newStatus, adminId);
        return TypedResults.Ok(await LoadDetailAsync(db, id, ct));
    }

    private static async Task<IResult> BulkStatusAsync(
        BulkStatusRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time,
        ClinicStatusChanger statusChanger, EmailOutboxSignal outbox, IAdminNotifier notifier,
        ILogger<BulkStatusRequest> logger, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var ids = request.ClinicIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) errors.Add("clinicIds", "Vyberte alespoň jednu kliniku.");
        else if (ids.Count > MaxBulkSize) errors.Add("clinicIds", $"Najednou lze změnit nejvýše {MaxBulkSize} klinik.");
        if (request.Status is not { } target || !Enum.IsDefined(target)) errors.Add("status", "Vyberte nový stav.");
        var message = Rules.Optional(errors, "message", request.Message, 1000);
        if (request.Status == ClinicStatus.Rejected && message is null && !errors.Has("message"))
        {
            errors.Add("message", "Uveďte důvod zamítnutí. Zobrazí se všem vybraným klinikám.");
        }
        if (!errors.IsValid) return ApiProblems.Validation(errors);

        var newStatus = request.Status!.Value;
        var adminId = user.GetSubjectId();
        var now = time.GetUtcNow().UtcDateTime;
        var clinics = await db.Clinics.Where(c => ids.Contains(c.Id)).ToListAsync(ct);

        var updated = new List<Guid>();
        var skipped = new List<Guid>();
        foreach (var clinic in clinics)
        {
            if (clinic.Status == newStatus)
            {
                skipped.Add(clinic.Id);
                continue;
            }
            statusChanger.Apply(clinic, newStatus, message, adminId, now, bulk: true);
            updated.Add(clinic.Id);
        }
        var found = clinics.Select(c => c.Id).ToHashSet();
        var notFound = ids.Where(id => !found.Contains(id)).ToList();

        // All changes of the batch are one transaction: either every selected clinic changes, or none.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.Conflict(
                "Některou z vybraných klinik mezitím upravil někdo jiný. Načtěte prosím seznam znovu a akci opakujte.", "concurrency_conflict");
        }

        if (updated.Count > 0)
        {
            outbox.Notify();
            await notifier.ClinicsChangedAsync(new ClinicsChangedEvent(updated, "status"));
        }
        logger.LogInformation("Bulk status change to {Status} by administrator {AdminId}: {Updated} updated, {Skipped} skipped, {NotFound} not found",
            newStatus, adminId, updated.Count, skipped.Count, notFound.Count);
        return TypedResults.Ok(new BulkStatusResult(updated, skipped, notFound));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, ClaimsPrincipal user, AppDbContext db, TimeProvider time, IAdminNotifier notifier,
        ILogger<BulkDeleteRequest> logger, CancellationToken ct)
    {
        var result = await DeleteClinicsAsync([id], user.GetSubjectId(), db, time, ct);
        if (result.Deleted.Count == 0) return ApiProblems.NotFound("Klinika nebyla nalezena.");
        await notifier.ClinicsChangedAsync(new ClinicsChangedEvent(result.Deleted, "deleted"));
        logger.LogInformation("Clinic {ClinicId} deleted by administrator {AdminId}", id, user.GetSubjectId());
        return TypedResults.NoContent();
    }

    private static async Task<IResult> BulkDeleteAsync(
        BulkDeleteRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time, IAdminNotifier notifier,
        ILogger<BulkDeleteRequest> logger, CancellationToken ct)
    {
        var ids = request.ClinicIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) return ApiProblems.Validation("clinicIds", "Vyberte alespoň jednu kliniku.");
        if (ids.Count > MaxBulkSize) return ApiProblems.Validation("clinicIds", $"Najednou lze smazat nejvýše {MaxBulkSize} klinik.");

        var result = await DeleteClinicsAsync(ids, user.GetSubjectId(), db, time, ct);
        if (result.Deleted.Count > 0) await notifier.ClinicsChangedAsync(new ClinicsChangedEvent(result.Deleted, "deleted"));
        logger.LogInformation("Bulk delete by administrator {AdminId}: {Deleted} deleted, {NotFound} not found",
            user.GetSubjectId(), result.Deleted.Count, result.NotFound.Count);
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Permanently deletes clinics with their history, notes, messages, sessions and login codes (database cascades).
    /// Unsent notification emails are dropped. The audit log keeps its events (detached from the clinic) plus one
    /// "clinic deleted" event per clinic that names it, so it stays clear who deleted what and when.
    /// </summary>
    private static async Task<BulkDeleteResult> DeleteClinicsAsync(
        IReadOnlyList<Guid> ids, Guid adminId, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var clinics = await db.Clinics.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var deleted = clinics.Select(c => c.Id).ToList();
        if (deleted.Count > 0)
        {
            var now = time.GetUtcNow().UtcDateTime;
            // Everything below is saved by one SaveChanges, i.e. in one transaction.
            db.OutboundEmails.RemoveRange(await db.OutboundEmails
                .Where(e => e.ClinicId != null && deleted.Contains(e.ClinicId.Value) && e.SentAt == null)
                .ToListAsync(ct));
            foreach (var clinic in clinics)
            {
                db.Record(ActivityEventType.ClinicDeleted, now, administratorId: adminId,
                    detail: clinic.Name.Length > 480 ? clinic.Name[..480] : clinic.Name);
            }
            db.Clinics.RemoveRange(clinics);
            await db.SaveChangesAsync(ct);
        }
        return new BulkDeleteResult(deleted, ids.Except(deleted).ToList());
    }

    private static async Task<IResult> AddNoteAsync(
        Guid id, AddNoteRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var text = Rules.Required(errors, "text", request.Text, 2000);
        if (text is null) return ApiProblems.Validation(errors);

        if (!await db.Clinics.AnyAsync(c => c.Id == id, ct)) return ApiProblems.NotFound("Klinika nebyla nalezena.");

        var adminId = user.GetSubjectId();
        var now = time.GetUtcNow().UtcDateTime;
        var note = new AdminNote { Id = Guid.CreateVersion7(), ClinicId = id, AdministratorId = adminId, Text = text, CreatedAt = now };
        db.AdminNotes.Add(note);
        db.Record(ActivityEventType.AdminNoteAdded, now, id, adminId);
        await db.SaveChangesAsync(ct);

        var username = await db.Administrators.Where(a => a.Id == adminId).Select(a => a.Username).FirstAsync(ct);
        return TypedResults.Created($"/api/admin/clinics/{id}", new AdminNoteDto(note.Id, note.Text, note.CreatedAt, username));
    }

    private static async Task<AdminClinicDetailDto?> LoadDetailAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (clinic is null) return null;

        var history = await db.ClinicStatusChanges.AsNoTracking()
            .Where(s => s.ClinicId == id)
            .OrderByDescending(s => s.ChangedAt)
            .Select(s => new StatusChangeDto(s.Id, s.FromStatus, s.ToStatus, s.ChangedAt, s.Reason, s.Administrator!.Username))
            .ToListAsync(ct);

        var notes = await db.AdminNotes.AsNoTracking()
            .Where(n => n.ClinicId == id)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new AdminNoteDto(n.Id, n.Text, n.CreatedAt, n.Administrator!.Username))
            .ToListAsync(ct);

        var lastSignIn = await db.ActivityEvents.AsNoTracking()
            .Where(e => e.ClinicId == id && e.Type == ActivityEventType.ClinicSignedIn)
            .MaxAsync(e => (DateTime?)e.OccurredAt, ct);

        var allowed = Enum.GetValues<ClinicStatus>().Where(s => s != clinic.Status).ToList();
        var location = clinic is { Latitude: { } lat, Longitude: { } lon } ? new LocationDto(lat, lon) : null;

        return new AdminClinicDetailDto(ClinicProfileDto.From(clinic), clinic.Version,
            clinic.PrivacyNoticeAcceptedAt, clinic.PrivacyNoticeVersion, lastSignIn, allowed, history, notes, location, clinic.IsDemo);
    }

    internal static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
