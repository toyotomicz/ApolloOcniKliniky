using ApolloData.Api.Auth;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using ApolloData.Api.Plans;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ApolloData.Api.Clinics;

public sealed record PlanDto(
    string Code, string Name, string Tagline, string Description,
    IReadOnlyList<string> Features, IReadOnlyList<string> FieldGroups, bool Highlighted, string? Badge);

public sealed record RegistrationResponse(string Email, bool VerificationEmailSent, int CodeLifetimeMinutes);

public static class PublicEndpoints
{
    /// <summary>Version of the privacy notice the registration form links to. Stored with each registration.</summary>
    public const string PrivacyNoticeVersion = "2026-10-demo";

    public static void MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/plans", () => TypedResults.Ok(PlanCatalog.All.Select(p =>
                new PlanDto(p.Code, p.Name, p.Tagline, p.Description, p.Features, p.FieldGroups, p.Highlighted, p.Badge))))
            .WithTags("Public");

        // Issues the antiforgery request token that the SPA sends in the X-CSRF-TOKEN header on unsafe requests.
        app.MapGet("/api/security/csrf", (HttpContext http, IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(http);
                return TypedResults.Ok(new { token = tokens.RequestToken });
            })
            .WithTags("Public");

        app.MapPost("/api/registrations", RegisterAsync)
            .RequireRateLimiting(RateLimitPolicies.Registration)
            .WithTags("Public");
    }

    private static async Task<IResult> RegisterAsync(
        RegistrationRequest request,
        AppDbContext db,
        ClinicOtpService otp,
        TimeProvider time,
        IOptions<AuthOptions> authOptions,
        ILogger<RegistrationRequest> logger,
        CancellationToken ct)
    {
        var (valid, errors) = ClinicValidator.ValidateRegistration(request);
        if (valid is null) return ApiProblems.Validation(errors);

        var normalizedEmail = Rules.NormalizeEmail(valid.Email);
        // Note: registration necessarily reveals that an email is taken; the login flow does not.
        if (await db.Clinics.AnyAsync(c => c.NormalizedEmail == normalizedEmail, ct))
        {
            return DuplicateEmail();
        }

        var now = time.GetUtcNow().UtcDateTime;
        var c = valid.Contact;
        var clinic = new Clinic
        {
            Id = Guid.CreateVersion7(),
            Name = valid.Name,
            OrganizationId = valid.OrganizationId,
            Email = valid.Email,
            NormalizedEmail = normalizedEmail,
            ContactFirstName = c.ContactFirstName,
            ContactLastName = c.ContactLastName,
            Phone = c.Phone,
            Street = c.Street,
            City = c.City,
            PostalCode = c.PostalCode,
            PlanCode = valid.Plan.Code,
            OperationalContact = c.OperationalContact,
            ServiceConfiguration = c.ServiceConfiguration,
            Status = ClinicStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            PrivacyNoticeAcceptedAt = now,
            PrivacyNoticeVersion = PrivacyNoticeVersion,
        };
        db.Clinics.Add(clinic);
        db.Record(ActivityEventType.ClinicRegistered, now, clinicId: clinic.Id, detail: valid.Plan.Code);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return DuplicateEmail();
        }

        logger.LogInformation("Clinic {ClinicId} registered with plan {Plan}", clinic.Id, clinic.PlanCode);

        // The verification code is sent right away so the user can verify without leaving the form.
        // A delivery failure does not roll back the registration; the user can request a new code.
        var issue = await otp.IssueAsync(clinic, ct);
        return TypedResults.Created("/api/clinic/me",
            new RegistrationResponse(clinic.Email, issue == CodeIssueResult.Sent, authOptions.Value.OtpLifetimeMinutes));
    }

    private static IResult DuplicateEmail() => ApiProblems.Conflict(
        "Klinika s tímto e-mailem je již registrována. Přihlaste se jednorázovým kódem.", "email_taken", "email");
}
