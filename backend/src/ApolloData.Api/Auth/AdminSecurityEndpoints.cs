using System.Security.Claims;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Auth;

public sealed record TwoFactorStatusDto(bool Enabled, DateTime? EnabledAt, int RecoveryCodesRemaining);

/// <param name="Secret">Base32 secret grouped by four characters, for manual entry into the authenticator app.</param>
public sealed record TwoFactorSetupDto(string Secret, string OtpAuthUri, string QrCodeDataUri);

public sealed record TwoFactorCodeRequest(string? Code);

/// <summary>Shown exactly once; only hashes are stored.</summary>
public sealed record RecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

/// <summary>Self-service two-factor settings of the signed-in administrator.</summary>
public static class AdminSecurityEndpoints
{
    private const string InvalidCodeMessage = "Kód není platný. Zadejte aktuální šestimístný kód z aplikace.";

    public static void MapAdminSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/security/two-factor")
            .RequireAuthorization(AuthPolicies.Admin)
            .WithTags("Administrator security");

        group.MapGet("", StatusAsync);
        group.MapPost("/setup", SetupAsync);
        group.MapPost("/enable", EnableAsync).RequireRateLimiting(RateLimitPolicies.AdminLogin);
        group.MapPost("/disable", DisableAsync).RequireRateLimiting(RateLimitPolicies.AdminLogin);
        group.MapPost("/recovery-codes", RenewRecoveryCodesAsync).RequireRateLimiting(RateLimitPolicies.AdminLogin);
    }

    private static async Task<IResult> StatusAsync(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var id = user.GetSubjectId();
        var admin = await db.Administrators.AsNoTracking().FirstAsync(a => a.Id == id, ct);
        var remaining = await db.AdminRecoveryCodes.CountAsync(c => c.AdministratorId == id && c.UsedAt == null, ct);
        return TypedResults.Ok(new TwoFactorStatusDto(admin.TwoFactorEnabled, admin.TwoFactorEnabledAt, remaining));
    }

    private static async Task<IResult> SetupAsync(ClaimsPrincipal user, AppDbContext db, TwoFactorService twoFactor, CancellationToken ct)
    {
        var admin = await LoadAsync(user, db, ct);
        if (admin.TwoFactorEnabled)
        {
            return ApiProblems.Conflict("Dvoufázové ověření je již zapnuté.", "two_factor_enabled");
        }

        // A new secret on every setup start; it only becomes active after a valid code confirms it.
        var secret = twoFactor.CreateSecret();
        admin.TwoFactorPendingSecret = secret.Protected;
        await db.SaveChangesAsync(ct);

        var uri = twoFactor.OtpAuthUri(admin.Username, secret.Base32);
        return TypedResults.Ok(new TwoFactorSetupDto(TwoFactorService.FormatForDisplay(secret.Base32), uri, TwoFactorService.QrCodeDataUri(uri)));
    }

    private static async Task<IResult> EnableAsync(
        TwoFactorCodeRequest request, ClaimsPrincipal user, AppDbContext db, TwoFactorService twoFactor, TimeProvider time, CancellationToken ct)
    {
        var admin = await LoadAsync(user, db, ct);
        if (admin.TwoFactorEnabled) return ApiProblems.Conflict("Dvoufázové ověření je již zapnuté.", "two_factor_enabled");
        if (admin.TwoFactorPendingSecret is null)
        {
            return ApiProblems.BadRequest("Nejdříve spusťte nastavení a naskenujte QR kód.", "two_factor_setup_missing");
        }

        var secret = twoFactor.Unprotect(admin.TwoFactorPendingSecret);
        if (twoFactor.VerifyCode(secret, request.Code, lastUsedStep: null) is not { } step)
        {
            return ApiProblems.Validation("code", InvalidCodeMessage);
        }

        var now = time.GetUtcNow().UtcDateTime;
        admin.TwoFactorEnabled = true;
        admin.TwoFactorSecret = admin.TwoFactorPendingSecret;
        admin.TwoFactorPendingSecret = null;
        admin.TwoFactorEnabledAt = now;
        admin.TwoFactorLastTimeStep = step;
        var codes = await ReplaceRecoveryCodesAsync(db, admin.Id, now, ct);
        db.Record(ActivityEventType.AdminTwoFactorEnabled, now, administratorId: admin.Id);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new RecoveryCodesDto(codes));
    }

    private static async Task<IResult> DisableAsync(
        TwoFactorCodeRequest request, ClaimsPrincipal user, AppDbContext db, TwoFactorService twoFactor, TimeProvider time, CancellationToken ct)
    {
        var admin = await LoadAsync(user, db, ct);
        if (!admin.TwoFactorEnabled) return ApiProblems.Conflict("Dvoufázové ověření není zapnuté.", "two_factor_disabled");
        if (!await VerifyCodeOrRecoveryAsync(admin, request.Code, db, twoFactor, time, ct))
        {
            return ApiProblems.Validation("code", "Kód není platný. Zadejte kód z aplikace nebo nepoužitý záložní kód.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        AdministratorAccounts.DisableTwoFactor(admin);
        await db.AdminRecoveryCodes.Where(c => c.AdministratorId == admin.Id).ExecuteDeleteAsync(ct);
        db.Record(ActivityEventType.AdminTwoFactorDisabled, now, administratorId: admin.Id);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RenewRecoveryCodesAsync(
        TwoFactorCodeRequest request, ClaimsPrincipal user, AppDbContext db, TwoFactorService twoFactor, TimeProvider time, CancellationToken ct)
    {
        var admin = await LoadAsync(user, db, ct);
        if (!admin.TwoFactorEnabled || admin.TwoFactorSecret is null)
        {
            return ApiProblems.Conflict("Dvoufázové ověření není zapnuté.", "two_factor_disabled");
        }
        if (twoFactor.VerifyCode(twoFactor.Unprotect(admin.TwoFactorSecret), request.Code, admin.TwoFactorLastTimeStep) is not { } step)
        {
            return ApiProblems.Validation("code", InvalidCodeMessage);
        }

        var now = time.GetUtcNow().UtcDateTime;
        admin.TwoFactorLastTimeStep = step;
        var codes = await ReplaceRecoveryCodesAsync(db, admin.Id, now, ct);
        db.Record(ActivityEventType.AdminRecoveryCodesRenewed, now, administratorId: admin.Id);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new RecoveryCodesDto(codes));
    }

    private static Task<Administrator> LoadAsync(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var id = user.GetSubjectId();
        return db.Administrators.FirstAsync(a => a.Id == id, ct);
    }

    private static async Task<bool> VerifyCodeOrRecoveryAsync(
        Administrator admin, string? code, AppDbContext db, TwoFactorService twoFactor, TimeProvider time, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || admin.TwoFactorSecret is null) return false;

        if (twoFactor.VerifyCode(twoFactor.Unprotect(admin.TwoFactorSecret), code, admin.TwoFactorLastTimeStep) is { } step)
        {
            admin.TwoFactorLastTimeStep = step;
            return true;
        }

        var hash = TwoFactorService.HashRecoveryCode(code);
        var recovery = await db.AdminRecoveryCodes.FirstOrDefaultAsync(c => c.AdministratorId == admin.Id && c.UsedAt == null && c.CodeHash == hash, ct);
        if (recovery is null) return false;
        recovery.UsedAt = time.GetUtcNow().UtcDateTime;
        return true;
    }

    private static async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(AppDbContext db, Guid adminId, DateTime now, CancellationToken ct)
    {
        await db.AdminRecoveryCodes.Where(c => c.AdministratorId == adminId).ExecuteDeleteAsync(ct);
        var codes = TwoFactorService.CreateRecoveryCodes();
        db.AdminRecoveryCodes.AddRange(codes.Select(code => new AdminRecoveryCode
        {
            Id = Guid.CreateVersion7(), AdministratorId = adminId, CodeHash = TwoFactorService.HashRecoveryCode(code), CreatedAt = now,
        }));
        return codes;
    }
}
