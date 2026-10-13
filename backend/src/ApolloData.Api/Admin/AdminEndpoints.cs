using System.Security.Claims;
using ApolloData.Api.Auth;
using ApolloData.Api.Clinics;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using ApolloData.Api.Plans;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Admin;

public sealed record ClinicListQuery(
    [FromQuery] string? Search,
    [FromQuery] string? Status,
    [FromQuery] string? Plan,
    [FromQuery] string? Sort,
    [FromQuery] string? Direction,
    [FromQuery] int? Page,
    [FromQuery] int? PageSize);

public sealed record ClinicListItemDto(
    Guid Id, string Name, string Email, string? OrganizationId, string City, string PlanCode,
    ClinicStatus Status, bool EmailVerified, DateTime CreatedAt);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record StatusChangeDto(Guid Id, ClinicStatus FromStatus, ClinicStatus ToStatus, DateTime ChangedAt, string? Reason, string AdministratorUsername);

public sealed record AdminNoteDto(Guid Id, string Text, DateTime CreatedAt, string AdministratorUsername);

public sealed record AdminClinicDetailDto(
    ClinicProfileDto Profile,
    uint Version,
    DateTime PrivacyNoticeAcceptedAt,
    string PrivacyNoticeVersion,
    DateTime? LastSignInAt,
    IReadOnlyList<ClinicStatus> AllowedStatusChanges,
    IReadOnlyList<StatusChangeDto> StatusHistory,
    IReadOnlyList<AdminNoteDto> Notes);

public sealed record ChangeStatusRequest(ClinicStatus? Status, string? Message, string? InternalNote, uint? Version);

public sealed record AddNoteRequest(string? Text);

public static class AdminEndpoints
{
    public const int MaxPageSize = 100;

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization(AuthPolicies.Admin)
            .WithTags("Administration");

        group.MapGet("/dashboard", (DashboardService dashboard, CancellationToken ct) => dashboard.GetAsync(ct));
        group.MapGet("/clinics", ListAsync);
        group.MapGet("/clinics/{id:guid}", DetailAsync);
        group.MapPost("/clinics/{id:guid}/status", ChangeStatusAsync);
        group.MapPost("/clinics/{id:guid}/notes", AddNoteAsync);
    }

    private static async Task<IResult> ListAsync([AsParameters] ClinicListQuery query, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        ClinicStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (Enum.TryParse<ClinicStatus>(query.Status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) status = parsed;
            else errors.Add("status", "Neznámý stav.");
        }
        string? plan = null;
        if (!string.IsNullOrWhiteSpace(query.Plan))
        {
            plan = PlanCatalog.Find(query.Plan)?.Code;
            if (plan is null) errors.Add("plan", "Neznámý plán.");
        }
        var search = query.Search?.Trim();
        if (search is { Length: > 100 }) errors.Add("search", "Hledaný výraz je příliš dlouhý.");
        if (!errors.IsValid) return ApiProblems.Validation(errors);

        var page = Math.Max(1, query.Page ?? 1);
        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, MaxPageSize);

        IQueryable<Clinic> clinics = db.Clinics.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            // Wildcards in user input are escaped; ILIKE runs as a parameterized query.
            var pattern = $"%{EscapeLike(search)}%";
            var compact = search.Replace(" ", "");
            clinics = clinics.Where(c =>
                EF.Functions.ILike(c.Name, pattern)
                || EF.Functions.ILike(c.NormalizedEmail, pattern)
                || (c.OrganizationId != null && c.OrganizationId.StartsWith(compact)));
        }
        if (status is not null) clinics = clinics.Where(c => c.Status == status);
        if (plan is not null) clinics = clinics.Where(c => c.PlanCode == plan);

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
                c.Status, c.EmailVerifiedAt != null, c.CreatedAt))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<ClinicListItemDto>(items, page, pageSize, total,
            (int)Math.Ceiling(total / (double)pageSize)));
    }

    private static async Task<IResult> DetailAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var detail = await LoadDetailAsync(db, id, ct);
        return detail is null ? ApiProblems.NotFound("Klinika nebyla nalezena.") : TypedResults.Ok(detail);
    }

    private static async Task<IResult> ChangeStatusAsync(
        Guid id, ChangeStatusRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time,
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

        clinic.Status = newStatus;
        clinic.StatusMessage = message;
        clinic.StatusChangedAt = now;
        clinic.UpdatedAt = now;

        db.ClinicStatusChanges.Add(new ClinicStatusChange
        {
            Id = Guid.CreateVersion7(),
            ClinicId = clinic.Id,
            FromStatus = previous,
            ToStatus = newStatus,
            AdministratorId = adminId,
            ChangedAt = now,
            Reason = message,
        });
        if (internalNote is not null)
        {
            db.AdminNotes.Add(new AdminNote
            {
                Id = Guid.CreateVersion7(), ClinicId = clinic.Id, AdministratorId = adminId, Text = internalNote, CreatedAt = now,
            });
        }
        db.Record(ActivityEventType.ClinicStatusChanged, now, clinic.Id, adminId, $"{previous}→{newStatus}");

        // A single SaveChanges runs in one database transaction: status, audit record, note and
        // activity event are committed together or not at all.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.Conflict(
                "Záznam mezitím upravil někdo jiný. Načtěte prosím aktuální údaje a akci opakujte.", "concurrency_conflict");
        }

        logger.LogInformation("Clinic {ClinicId} status changed {From} -> {To} by administrator {AdminId}",
            clinic.Id, previous, newStatus, adminId);
        return TypedResults.Ok(await LoadDetailAsync(db, id, ct));
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

        return new AdminClinicDetailDto(ClinicProfileDto.From(clinic), clinic.Version,
            clinic.PrivacyNoticeAcceptedAt, clinic.PrivacyNoticeVersion, lastSignIn, allowed, history, notes);
    }

    internal static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
