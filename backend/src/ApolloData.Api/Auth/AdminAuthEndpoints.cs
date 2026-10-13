using System.Security.Claims;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Auth;

public sealed record AdminLoginRequest(string? Username, string? Password);

public sealed record AdminMeResponse(Guid Id, string Username, DateTime? LastLoginAt);

public static class AdminAuthEndpoints
{
    public const string LoginFailedMessage = "Přihlášení se nezdařilo. Zkontrolujte údaje, případně to zkuste později.";

    public static void MapAdminAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin-auth").WithTags("Administrator authentication");

        group.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimitPolicies.AdminLogin);

        group.MapPost("/logout", async (HttpContext http, SessionService sessions, CancellationToken ct) =>
        {
            await sessions.SignOutAsync(http, AuthSchemes.Admin, ct);
            return TypedResults.NoContent();
        });

        group.MapGet("/me", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
            {
                var id = user.GetSubjectId();
                var admin = await db.Administrators.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
                return admin is null
                    ? ApiProblems.Unauthorized("Relace vypršela.")
                    : TypedResults.Ok(new AdminMeResponse(admin.Id, admin.Username, admin.LastLoginAt));
            })
            .RequireAuthorization(AuthPolicies.Admin);
    }

    private static async Task<IResult> LoginAsync(
        AdminLoginRequest request, HttpContext http, AdministratorAccounts accounts, SessionService sessions,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 64) errors.Add("username", "Zadejte uživatelské jméno.");
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 256) errors.Add("password", "Zadejte heslo.");
        if (!errors.IsValid) return ApiProblems.Validation(errors);

        var (result, admin) = await accounts.VerifyLoginAsync(request.Username!, request.Password!, ct);
        if (result != AdminLoginResult.Success || admin is null)
        {
            return ApiProblems.Unauthorized(LoginFailedMessage);
        }

        db.Record(ActivityEventType.AdminSignedIn, time.GetUtcNow().UtcDateTime, administratorId: admin.Id);
        await db.SaveChangesAsync(ct);
        await sessions.SignInAdminAsync(http, admin, ct);
        return TypedResults.Ok(new AdminMeResponse(admin.Id, admin.Username, admin.LastLoginAt));
    }
}
