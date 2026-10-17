using ApolloData.Api.Auth;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using ApolloData.Api.Email;
using ApolloData.Api.Geo;
using ApolloData.Api.Plans;
using ApolloData.Api.Realtime;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ApolloData.Api.Clinics;

public sealed record PlanDto(
    string Code, string Name, string Tagline, string Description,
    IReadOnlyList<PlanFeature> Features, IReadOnlyList<string> FieldGroups, bool Highlighted, string? Badge);

public sealed record RegistrationCodeRequest(string? Email, string? ContactName);

public sealed record RegistrationCodeResponse(string Email, int CodeLifetimeMinutes, int ResendAfterSeconds);

public sealed record RegistrationCodeConfirmRequest(string? Email, string? Code);

public sealed record RegistrationCodeConfirmResponse(string Email, string VerificationToken, DateTime ExpiresAt);

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

        // Step 2 of the registration: the e-mail is verified with a one-time code before the registration exists.
        app.MapPost("/api/registrations/email-code", SendEmailCodeAsync)
            .RequireRateLimiting(RateLimitPolicies.OtpRequest)
            .WithTags("Public");
        app.MapPost("/api/registrations/email-code/verify", VerifyEmailCodeAsync)
            .RequireRateLimiting(RateLimitPolicies.OtpVerify)
            .WithTags("Public");

        app.MapPost("/api/registrations", RegisterAsync)
            .RequireRateLimiting(RateLimitPolicies.Registration)
            .WithTags("Public");
    }

    private static async Task<IResult> SendEmailCodeAsync(
        RegistrationCodeRequest request, AppDbContext db, RegistrationEmailVerifier verifier, IOptions<AuthOptions> options, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var email = Rules.Email(errors, "email", request.Email);
        if (email is null) return ApiProblems.Validation(errors);

        var normalized = Rules.NormalizeEmail(email);
        if (await db.Clinics.AnyAsync(c => c.NormalizedEmail == normalized, ct)) return DuplicateEmail();

        var contactName = request.ContactName?.Trim() is { Length: > 0 and <= 200 } name ? name : null;
        return await verifier.IssueAsync(email, contactName, ct) switch
        {
            CodeIssueResult.Sent => TypedResults.Accepted((string?)null,
                new RegistrationCodeResponse(email, options.Value.OtpLifetimeMinutes, options.Value.OtpResendCooldownSeconds)),
            CodeIssueResult.Throttled => TypedResults.Problem(
                detail: "Kód jsme na tuto adresu poslali před chvílí. Použijte kód z posledního e-mailu, nový si můžete vyžádat za minutu.",
                statusCode: StatusCodes.Status429TooManyRequests, title: "Příliš mnoho požadavků.",
                extensions: new Dictionary<string, object?> { ["code"] = "code_throttled" }),
            _ => ApiProblems.ServiceUnavailable("E-mail s kódem se nepodařilo odeslat. Zkuste to prosím za chvíli znovu.", "email_delivery_failed"),
        };
    }

    private static async Task<IResult> VerifyEmailCodeAsync(
        RegistrationCodeConfirmRequest request, RegistrationEmailVerifier verifier, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var email = Rules.Email(errors, "email", request.Email);
        var code = request.Code?.Trim();
        if (code is null || code.Length != 6 || !code.All(char.IsAsciiDigit)) errors.Add("code", "Kód má 6 číslic.");
        if (!errors.IsValid) return ApiProblems.Validation(errors);

        var token = await verifier.VerifyAsync(email!, code!, ct);
        return token is null
            ? ApiProblems.BadRequest(ClinicAuthEndpoints.InvalidCodeMessage, "invalid_code")
            : TypedResults.Ok(new RegistrationCodeConfirmResponse(email!, token.Token, token.ExpiresAt));
    }

    private static async Task<IResult> RegisterAsync(
        RegistrationRequest request,
        HttpContext http,
        AppDbContext db,
        RegistrationEmailVerifier verifier,
        SessionService sessions,
        TimeProvider time,
        AppInfo app,
        EmailOutboxSignal outbox,
        GeocodingSignal geocoding,
        IAdminNotifier notifier,
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

        var verification = await verifier.FindVerifiedAsync(valid.Email, request.EmailVerificationToken, ct);
        if (verification is null)
        {
            return TypedResults.Problem(
                detail: "E-mail není ověřen nebo jeho ověření vypršelo. Ověřte jej prosím znovu kódem.",
                statusCode: StatusCodes.Status400BadRequest, title: "Neplatný požadavek.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "email_not_verified",
                    ["errors"] = new Dictionary<string, string[]> { ["email"] = ["E-mail je potřeba ověřit kódem."] },
                });
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
            PublicEmails = c.PublicContacts.Emails,
            PublicPhones = c.PublicContacts.Phones,
            Website = c.PublicContacts.Website,
            PatientInfo = c.PublicContacts.PatientInfo,
            Status = ClinicStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            EmailVerifiedAt = verification.VerifiedAt,
            AuthorityConfirmedAt = now,
            PrivacyNoticeAcceptedAt = now,
            PrivacyNoticeVersion = PrivacyNoticeVersion,
        };
        db.Clinics.Add(clinic);
        verification.ConsumedAt = now;
        db.Record(ActivityEventType.ClinicRegistered, now, clinicId: clinic.Id, detail: valid.Plan.Code);
        db.Record(ActivityEventType.ClinicEmailVerified, now, clinicId: clinic.Id);
        db.Record(ActivityEventType.ClinicSignedIn, now, clinicId: clinic.Id);
        // Confirmation with the PDF overview; committed together with the registration and sent by the outbox worker.
        db.EnqueueEmail(OutboundEmailKind.RegistrationConfirmation, clinic,
            EmailTemplates.RegistrationConfirmation(app.Name, clinic, app.ClinicLoginUrl), now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return DuplicateEmail();
        }

        logger.LogInformation("Clinic {ClinicId} registered with plan {Plan}", clinic.Id, clinic.PlanCode);
        outbox.Notify();
        geocoding.Notify();
        await notifier.RegistrationCreatedAsync(new RegistrationCreatedEvent(clinic.Id, clinic.Name, clinic.PlanCode, clinic.City, clinic.CreatedAt));

        // The e-mail is already verified, so the clinic goes straight to its portal.
        await sessions.SignInClinicAsync(http, clinic, ct);
        return TypedResults.Created("/api/clinic/me", ClinicProfileDto.From(clinic));
    }

    private static IResult DuplicateEmail() => ApiProblems.Conflict(
        "Klinika s tímto e-mailem je již registrována. Přihlaste se jednorázovým kódem.", "email_taken", "email");
}
