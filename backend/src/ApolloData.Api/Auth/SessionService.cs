using System.Security.Claims;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApolloData.Api.Auth;

/// <summary>
/// Creates and revokes server-side sessions. The (encrypted, HttpOnly) cookie only identifies the session;
/// every request re-validates it against the database, so logout and revocation take effect immediately.
/// </summary>
public sealed class SessionService(AppDbContext db, TimeProvider time, IOptions<AuthOptions> options)
{
    public async Task SignInClinicAsync(HttpContext http, Clinic clinic, CancellationToken ct)
    {
        var session = await CreateAsync(SessionKind.Clinic, clinicId: clinic.Id, administratorId: null,
            TimeSpan.FromHours(options.Value.ClinicSessionHours), ct);
        await SignInAsync(http, AuthSchemes.Clinic, session, clinic.Id, AppClaims.ClinicRole);
    }

    public async Task SignInAdminAsync(HttpContext http, Administrator admin, CancellationToken ct)
    {
        var session = await CreateAsync(SessionKind.Administrator, clinicId: null, administratorId: admin.Id,
            TimeSpan.FromHours(options.Value.AdminSessionHours), ct);
        await SignInAsync(http, AuthSchemes.Admin, session, admin.Id, AppClaims.AdminRole,
            new Claim(ClaimTypes.Name, admin.Username));
    }

    /// <summary>Revokes the session behind the given scheme's cookie (if any) and clears the cookie.</summary>
    public async Task SignOutAsync(HttpContext http, string scheme, CancellationToken ct)
    {
        var result = await http.AuthenticateAsync(scheme);
        if (result.Succeeded && Guid.TryParse(result.Principal.FindFirstValue(AppClaims.SessionId), out var sessionId))
        {
            var now = time.GetUtcNow().UtcDateTime;
            await db.UserSessions
                .Where(s => s.Id == sessionId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        }
        await http.SignOutAsync(scheme);
    }

    public Task<int> RevokeAllForAdministratorAsync(Guid administratorId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return db.UserSessions
            .Where(s => s.AdministratorId == administratorId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
    }

    private async Task<UserSession> CreateAsync(SessionKind kind, Guid? clinicId, Guid? administratorId, TimeSpan lifetime, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ClinicId = clinicId,
            AdministratorId = administratorId,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    private static Task SignInAsync(HttpContext http, string scheme, UserSession session, Guid subject, string role, params Claim[] extra)
    {
        Claim[] claims =
        [
            new(AppClaims.SessionId, session.Id.ToString()),
            new(AppClaims.Subject, subject.ToString()),
            new(AppClaims.Role, role),
            .. extra,
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme, ClaimTypes.Name, AppClaims.Role));
        return http.SignInAsync(scheme, principal, new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = new DateTimeOffset(session.ExpiresAt, TimeSpan.Zero),
            AllowRefresh = false,
        });
    }

    /// <summary>Cookie event handler that validates the referenced session on every request.</summary>
    internal static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context, SessionKind expectedKind)
    {
        var principal = context.Principal;
        if (principal is null || !Guid.TryParse(principal.FindFirstValue(AppClaims.SessionId), out var sessionId))
        {
            await RejectAsync(context);
            return;
        }

        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        var valid = expectedKind == SessionKind.Clinic
            ? await db.UserSessions.AnyAsync(s => s.Id == sessionId && s.Kind == SessionKind.Clinic
                                                  && s.RevokedAt == null && s.ExpiresAt > now)
            : await db.UserSessions.AnyAsync(s => s.Id == sessionId && s.Kind == SessionKind.Administrator
                                                  && s.RevokedAt == null && s.ExpiresAt > now
                                                  && db.Administrators.Any(a => a.Id == s.AdministratorId && a.IsActive));
        if (!valid)
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetSubjectId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(AppClaims.Subject), out var id)
            ? id
            : throw new InvalidOperationException("Authenticated principal has no subject id.");
}
