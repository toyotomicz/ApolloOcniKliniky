using System.Security.Claims;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApolloData.Api.Auth;

public sealed record AdminLoginRequest(string? Username, string? Password);

public sealed record TwoFactorLoginRequest(string? Code, string? RecoveryCode);

public sealed record AdminMeResponse(Guid Id, string Username, DateTime? LastLoginAt, bool TwoFactorEnabled);

/// <summary>Returned with 202 when the password was correct but the second factor is still required.</summary>
public sealed record TwoFactorRequiredResponse(bool TwoFactorRequired, int ExpiresInSeconds);

public static class AdminAuthEndpoints
{
    public const string LoginFailedMessage = "Přihlášení se nezdařilo. Zkontrolujte údaje, případně to zkuste později.";
    public const string TwoFactorFailedMessage = "Ověřovací kód není platný. Zkontrolujte čas v telefonu, případně použijte záložní kód.";
    public const string TwoFactorExpiredMessage = "Platnost přihlášení vypršela. Zadejte prosím znovu jméno a heslo.";

    public static void MapAdminAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin-auth").WithTags("Administrator authentication");

        group.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimitPolicies.AdminLogin);
        group.MapPost("/login/two-factor", TwoFactorAsync).RequireRateLimiting(RateLimitPolicies.AdminLogin);

        group.MapPost("/logout", async (HttpContext http, SessionService sessions, AppDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var auth = await http.AuthenticateAsync(AuthSchemes.Admin);
            if (auth.Succeeded)
            {
                db.Record(ActivityEventType.AdminSignedOut, time.GetUtcNow().UtcDateTime, administratorId: auth.Principal.GetSubjectId());
                await db.SaveChangesAsync(ct);
            }
            await sessions.SignOutAsync(http, AuthSchemes.Admin, ct);
            return TypedResults.NoContent();
        });

        group.MapGet("/me", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
            {
                var id = user.GetSubjectId();
                var admin = await db.Administrators.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
                return admin is null ? ApiProblems.Unauthorized("Relace vypršela.") : TypedResults.Ok(ToMe(admin));
            })
            .RequireAuthorization(AuthPolicies.Admin);
    }

    public static AdminMeResponse ToMe(Administrator admin) => new(admin.Id, admin.Username, admin.LastLoginAt, admin.TwoFactorEnabled);

    private static async Task<IResult> LoginAsync(
        AdminLoginRequest request, HttpContext http, AdministratorAccounts accounts, SessionService sessions,
        PendingTwoFactorLogin pending, IOptions<AuthOptions> authOptions, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 64) errors.Add("username", "Zadejte uživatelské jméno.");
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 256) errors.Add("password", "Zadejte heslo.");
        if (!errors.IsValid) return ApiProblems.Validation(errors);

        var now = time.GetUtcNow().UtcDateTime;
        var check = await accounts.VerifyLoginAsync(request.Username!, request.Password!, ct);
        if (check.Result != AdminLoginResult.Success || check.Admin is not { } admin)
        {
            // The attempted username is not stored: people sometimes type their password into that field.
            db.Record(ActivityEventType.AdminSignInFailed, now, administratorId: check.FailedAdministratorId,
                detail: check.FailedAdministratorId is null ? "unknown-user" : check.Locked ? "password;locked" : "password");
            await db.SaveChangesAsync(ct);
            return ApiProblems.Unauthorized(LoginFailedMessage);
        }

        if (admin.TwoFactorEnabled)
        {
            pending.Issue(http, admin.Id, authOptions.Value.SecureCookies == true);
            return TypedResults.Accepted((string?)null,
                new TwoFactorRequiredResponse(true, (int)PendingTwoFactorLogin.Lifetime.TotalSeconds));
        }

        accounts.MarkSignedIn(admin);
        db.Record(ActivityEventType.AdminSignedIn, now, administratorId: admin.Id, detail: "password");
        await db.SaveChangesAsync(ct);
        await sessions.SignInAdminAsync(http, admin, ct);
        return TypedResults.Ok(ToMe(admin));
    }

    private static async Task<IResult> TwoFactorAsync(
        TwoFactorLoginRequest request, HttpContext http, AdministratorAccounts accounts, SessionService sessions,
        PendingTwoFactorLogin pending, TwoFactorService twoFactor, IOptions<AuthOptions> authOptions,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var secureCookies = authOptions.Value.SecureCookies == true;
        if (pending.Read(http) is not { } adminId)
        {
            return ApiProblems.Unauthorized(TwoFactorExpiredMessage);
        }

        var useRecovery = !string.IsNullOrWhiteSpace(request.RecoveryCode);
        if (!useRecovery && string.IsNullOrWhiteSpace(request.Code))
        {
            return ApiProblems.Validation("code", "Zadejte šestimístný kód z aplikace.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var admin = await db.Administrators.FirstOrDefaultAsync(a => a.Id == adminId && a.IsActive, ct);
        if (admin is null || !admin.TwoFactorEnabled || admin.TwoFactorSecret is null)
        {
            PendingTwoFactorLogin.Clear(http, secureCookies);
            return ApiProblems.Unauthorized(TwoFactorExpiredMessage);
        }
        if (AdministratorAccounts.IsLockedOut(admin, now))
        {
            db.Record(ActivityEventType.AdminSignInFailed, now, administratorId: admin.Id, detail: "2fa;locked");
            await db.SaveChangesAsync(ct);
            return ApiProblems.Unauthorized(LoginFailedMessage);
        }

        var method = "2fa";
        var valid = false;
        if (useRecovery)
        {
            var hash = TwoFactorService.HashRecoveryCode(request.RecoveryCode!);
            var recovery = await db.AdminRecoveryCodes
                .FirstOrDefaultAsync(c => c.AdministratorId == admin.Id && c.UsedAt == null && c.CodeHash == hash, ct);
            if (recovery is not null)
            {
                recovery.UsedAt = now;
                valid = true;
                method = "recovery-code";
            }
        }
        else if (twoFactor.VerifyCode(twoFactor.Unprotect(admin.TwoFactorSecret), request.Code, admin.TwoFactorLastTimeStep) is { } step)
        {
            admin.TwoFactorLastTimeStep = step;
            valid = true;
        }

        if (!valid)
        {
            var locked = await accounts.RegisterFailedAttemptAsync(admin, ct);
            db.Record(ActivityEventType.AdminSignInFailed, now, administratorId: admin.Id, detail: locked ? $"{method};locked" : method);
            await db.SaveChangesAsync(ct);
            return ApiProblems.Unauthorized(TwoFactorFailedMessage);
        }

        accounts.MarkSignedIn(admin);
        db.Record(ActivityEventType.AdminSignedIn, now, administratorId: admin.Id, detail: method);
        await db.SaveChangesAsync(ct);
        PendingTwoFactorLogin.Clear(http, secureCookies);
        await sessions.SignInAdminAsync(http, admin, ct);
        return TypedResults.Ok(ToMe(admin));
    }
}
