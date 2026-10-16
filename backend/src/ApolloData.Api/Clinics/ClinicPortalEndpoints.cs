using System.Security.Claims;
using ApolloData.Api.Auth;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Admin;
using ApolloData.Api.Documents;
using ApolloData.Api.Domain;
using ApolloData.Api.Geo;
using ApolloData.Api.Messages;
using ApolloData.Api.Plans;
using ApolloData.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Clinics;

/// <summary>
/// Endpoints for an authenticated clinic. There is deliberately no clinic id in any route:
/// the clinic is always resolved from the server-side session, so one clinic cannot address another's data.
/// </summary>
public static class ClinicPortalEndpoints
{
    public static void MapClinicPortalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clinic")
            .RequireAuthorization(AuthPolicies.Clinic)
            .WithTags("Clinic portal");

        group.MapGet("/me", GetAsync);
        group.MapPut("/me", UpdateAsync);
        group.MapGet("/me/confirmation.pdf", ConfirmationAsync);
        group.MapClinicMessageEndpoints();
    }

    private static async Task<IResult> ConfirmationAsync(
        ClaimsPrincipal user, AppDbContext db, AppInfo app, ReportingClock clock, CancellationToken ct)
    {
        var clinicId = user.GetSubjectId();
        var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null) return ApiProblems.NotFound();

        db.Record(ActivityEventType.ConfirmationDownloaded, clock.UtcNow, clinicId: clinic.Id);
        await db.SaveChangesAsync(ct);
        var pdf = ConfirmationPdf.Generate(clinic, app.Name, clock.UtcNow, clock.TimeZone);
        return TypedResults.File(pdf, "application/pdf", ConfirmationPdf.FileName(clinic));
    }

    private static async Task<IResult> GetAsync(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var clinicId = user.GetSubjectId();
        var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        return clinic is null ? ApiProblems.NotFound() : TypedResults.Ok(ClinicProfileDto.From(clinic));
    }

    private static async Task<IResult> UpdateAsync(
        UpdateClinicProfileRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time,
        GeocodingSignal geocoding, IAdminNotifier notifier, CancellationToken ct)
    {
        var clinicId = user.GetSubjectId();
        var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null) return ApiProblems.NotFound();

        var plan = PlanCatalog.Find(clinic.PlanCode);
        if (plan is null) return ApiProblems.BadRequest("Úroveň registrace kliniky již není podporována. Kontaktujte administrátora.", "unknown_plan");

        var (valid, errors) = ClinicValidator.ValidateProfileUpdate(request, plan);
        if (valid is null) return ApiProblems.Validation(errors);

        var addressChanged = clinic.Street != valid.Street || clinic.City != valid.City || clinic.PostalCode != valid.PostalCode;

        // Only explicitly listed fields are copied (no over-posting of status, email, plan, ...).
        clinic.ContactFirstName = valid.ContactFirstName;
        clinic.ContactLastName = valid.ContactLastName;
        clinic.Phone = valid.Phone;
        clinic.Street = valid.Street;
        clinic.City = valid.City;
        clinic.PostalCode = valid.PostalCode;
        clinic.OperationalContact = valid.OperationalContact;
        clinic.ServiceConfiguration = valid.ServiceConfiguration;

        if (addressChanged) clinic.ResetLocation();

        var now = time.GetUtcNow().UtcDateTime;
        clinic.UpdatedAt = now;
        db.Record(ActivityEventType.ClinicProfileUpdated, now, clinicId: clinic.Id, detail: addressChanged ? "address" : null);
        await db.SaveChangesAsync(ct);

        if (addressChanged) geocoding.Notify();
        await notifier.ClinicsChangedAsync(new ClinicsChangedEvent([clinic.Id], "profile"));
        return TypedResults.Ok(ClinicProfileDto.From(clinic));
    }
}
