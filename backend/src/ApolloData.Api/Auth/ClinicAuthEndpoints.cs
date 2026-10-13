using System.Text.RegularExpressions;
using ApolloData.Api.Clinics;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApolloData.Api.Auth;

public sealed record RequestCodeRequest(string? Email);

public sealed record RequestCodeResponse(string Message, int CodeLifetimeMinutes, int ResendAfterSeconds);

public sealed record VerifyCodeRequest(string? Email, string? Code);

public static partial class ClinicAuthEndpoints
{
    public const string GenericCodeSentMessage =
        "Pokud je e-mail registrován, poslali jsme na něj jednorázový přihlašovací kód.";

    public const string InvalidCodeMessage =
        "Kód je neplatný nebo vypršela jeho platnost. Zkontrolujte jej, případně si vyžádejte nový.";

    public static void MapClinicAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clinic-auth").WithTags("Clinic authentication");

        group.MapPost("/request-code", RequestCodeAsync).RequireRateLimiting(RateLimitPolicies.OtpRequest);
        group.MapPost("/verify-code", VerifyCodeAsync).RequireRateLimiting(RateLimitPolicies.OtpVerify);
        group.MapPost("/logout", async (HttpContext http, SessionService sessions, CancellationToken ct) =>
        {
            await sessions.SignOutAsync(http, AuthSchemes.Clinic, ct);
            return TypedResults.NoContent();
        });
    }

    private static async Task<IResult> RequestCodeAsync(
        RequestCodeRequest request, AppDbContext db, ClinicOtpService otp, IOptions<AuthOptions> options, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var email = Rules.Email(errors, "email", request.Email);
        if (email is null) return ApiProblems.Validation(errors);

        var accepted = TypedResults.Accepted((string?)null, new RequestCodeResponse(
            GenericCodeSentMessage, options.Value.OtpLifetimeMinutes, options.Value.OtpResendCooldownSeconds));

        var normalized = Rules.NormalizeEmail(email);
        var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.NormalizedEmail == normalized, ct);
        if (clinic is null)
        {
            // Same response as for a registered address, so the endpoint does not reveal which emails exist.
            return accepted;
        }

        var result = await otp.IssueAsync(clinic, ct);
        return result == CodeIssueResult.DeliveryFailed
            ? ApiProblems.ServiceUnavailable(
                "E-mail s kódem se nepodařilo odeslat. Zkuste to prosím za chvíli znovu.", "email_delivery_failed")
            : accepted;
    }

    private static async Task<IResult> VerifyCodeAsync(
        VerifyCodeRequest request, HttpContext http, AppDbContext db, ClinicOtpService otp,
        SessionService sessions, TimeProvider time, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var email = Rules.Email(errors, "email", request.Email);
        var code = request.Code?.Trim();
        if (code is null || !CodePattern().IsMatch(code)) errors.Add("code", "Kód má 6 číslic.");
        if (!errors.IsValid) return ApiProblems.Validation(errors);

        var normalized = Rules.NormalizeEmail(email!);
        var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.NormalizedEmail == normalized, ct);
        if (clinic is null || await otp.VerifyAsync(clinic.Id, code!, ct) != CodeVerifyResult.Success)
        {
            return ApiProblems.BadRequest(InvalidCodeMessage, "invalid_code");
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (clinic.EmailVerifiedAt is null)
        {
            // Email verification is independent of administrative approval: status stays as it is.
            clinic.EmailVerifiedAt = now;
            db.Record(ActivityEventType.ClinicEmailVerified, now, clinicId: clinic.Id);
        }
        db.Record(ActivityEventType.ClinicSignedIn, now, clinicId: clinic.Id);
        await db.SaveChangesAsync(ct);

        await sessions.SignInClinicAsync(http, clinic, ct);
        return TypedResults.Ok(ClinicProfileDto.From(clinic));
    }

    [GeneratedRegex("^[0-9]{6}$")]
    private static partial Regex CodePattern();
}
