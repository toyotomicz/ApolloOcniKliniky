using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApolloData.Api.Auth;

public sealed class AdminConfigurationException(string message) : Exception(message);

public enum AdminLoginResult
{
    Success,
    Failed,
}

/// <param name="Admin">The administrator, when the password was correct.</param>
/// <param name="FailedAdministratorId">Known account whose login failed (for the audit log); null for unknown usernames.</param>
public sealed record AdminPasswordCheck(AdminLoginResult Result, Administrator? Admin, Guid? FailedAdministratorId, bool Locked);

/// <summary>
/// Administrator credential handling. Passwords are hashed with ASP.NET Core Identity's
/// <see cref="PasswordHasher{TUser}"/> (PBKDF2, salted, versioned format).
/// </summary>
public sealed class AdministratorAccounts(
    AppDbContext db,
    IPasswordHasher<Administrator> hasher,
    TimeProvider time,
    IOptions<AuthOptions> options,
    ILogger<AdministratorAccounts> logger)
{
    public const int MinPasswordLength = 12;

    // Used to keep timing similar for unknown usernames.
    private static readonly Administrator DummyAdmin = new() { Username = "-", NormalizedUsername = "-", PasswordHash = "-" };
    private static string? _dummyHash;

    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    /// <summary>
    /// Creates the initial administrator from ADMIN_USERNAME / ADMIN_PASSWORD when it does not exist yet.
    /// Idempotent: an existing administrator is never duplicated and its password is never reset here.
    /// </summary>
    public async Task SeedAsync(string? username, string? password, CancellationToken ct)
    {
        username = username?.Trim();
        var anyAdmin = await db.Administrators.AnyAsync(ct);

        if (string.IsNullOrEmpty(username))
        {
            if (!anyAdmin)
            {
                throw new AdminConfigurationException(
                    "No administrator exists and ADMIN_USERNAME is not configured. Set ADMIN_USERNAME and ADMIN_PASSWORD (see README).");
            }
            return;
        }

        var normalized = Normalize(username);
        if (await db.Administrators.AnyAsync(a => a.NormalizedUsername == normalized, ct))
        {
            logger.LogDebug("Administrator {Username} already exists; seeding skipped", username);
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            if (anyAdmin)
            {
                logger.LogWarning("ADMIN_USERNAME {Username} does not exist and ADMIN_PASSWORD is not set; no administrator created", username);
                return;
            }
            throw new AdminConfigurationException(
                "ADMIN_PASSWORD is not configured. The initial administrator cannot be created without it; no default password is used.");
        }

        EnsurePasswordPolicy(password);
        var admin = new Administrator
        {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = normalized,
            PasswordHash = "",
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        admin.PasswordHash = hasher.HashPassword(admin, password);
        db.Administrators.Add(admin);

        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Initial administrator {Username} created", username);
        }
        catch (DbUpdateException)
        {
            db.Entry(admin).State = EntityState.Detached;
            // Tolerate another instance seeding the same administrator concurrently.
            if (!await db.Administrators.AnyAsync(a => a.NormalizedUsername == normalized, ct)) throw;
        }
    }

    /// <summary>Explicit local maintenance command (not exposed over HTTP): creates or resets an administrator.</summary>
    public async Task<bool> ResetPasswordAsync(string username, string password, SessionService sessions, CancellationToken ct)
    {
        EnsurePasswordPolicy(password);
        var normalized = Normalize(username);
        var admin = await db.Administrators.FirstOrDefaultAsync(a => a.NormalizedUsername == normalized, ct);
        var created = admin is null;
        admin ??= db.Administrators.Add(new Administrator
        {
            Id = Guid.NewGuid(),
            Username = username.Trim(),
            NormalizedUsername = normalized,
            PasswordHash = "",
            CreatedAt = time.GetUtcNow().UtcDateTime,
        }).Entity;

        admin.PasswordHash = hasher.HashPassword(admin, password);
        admin.FailedLoginCount = 0;
        admin.LockoutEndAt = null;
        admin.IsActive = true;
        db.Record(ActivityEventType.AdminPasswordReset, time.GetUtcNow().UtcDateTime, administratorId: admin.Id, detail: "maintenance");
        await db.SaveChangesAsync(ct);

        if (!created) await sessions.RevokeAllForAdministratorAsync(admin.Id, ct);
        return created;
    }

    public async Task<AdminPasswordCheck> VerifyLoginAsync(string username, string password, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var normalized = Normalize(username);
        var admin = await db.Administrators.FirstOrDefaultAsync(a => a.NormalizedUsername == normalized, ct);

        if (admin is null || !admin.IsActive)
        {
            _dummyHash ??= hasher.HashPassword(DummyAdmin, Guid.NewGuid().ToString());
            hasher.VerifyHashedPassword(DummyAdmin, _dummyHash, password);
            logger.LogInformation("Administrator login failed: unknown or inactive account");
            return new AdminPasswordCheck(AdminLoginResult.Failed, null, admin?.Id, Locked: false);
        }

        if (IsLockedOut(admin, now))
        {
            logger.LogWarning("Administrator login rejected: account {AdminId} locked until {LockoutEnd:o}", admin.Id, admin.LockoutEndAt);
            return new AdminPasswordCheck(AdminLoginResult.Failed, null, admin.Id, Locked: true);
        }

        var verification = hasher.VerifyHashedPassword(admin, admin.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            var locked = await RegisterFailedAttemptAsync(admin, ct);
            return new AdminPasswordCheck(AdminLoginResult.Failed, null, admin.Id, locked);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            admin.PasswordHash = hasher.HashPassword(admin, password);
            await db.SaveChangesAsync(ct);
        }
        return new AdminPasswordCheck(AdminLoginResult.Success, admin, null, Locked: false);
    }

    public static bool IsLockedOut(Administrator admin, DateTime now) => admin.LockoutEndAt > now;

    /// <summary>Counts a failed password or second-factor attempt; locks the account after too many. Returns true when it locked.</summary>
    public async Task<bool> RegisterFailedAttemptAsync(Administrator admin, CancellationToken ct)
    {
        var locked = false;
        admin.FailedLoginCount++;
        if (admin.FailedLoginCount >= options.Value.AdminMaxFailedAttempts)
        {
            admin.LockoutEndAt = time.GetUtcNow().UtcDateTime.AddMinutes(options.Value.AdminLockoutMinutes);
            admin.FailedLoginCount = 0;
            locked = true;
            logger.LogWarning("Administrator account {AdminId} locked after repeated failed logins", admin.Id);
        }
        await db.SaveChangesAsync(ct);
        return locked;
    }

    /// <summary>Resets failure counters after all factors passed (saved by the caller).</summary>
    public void MarkSignedIn(Administrator admin)
    {
        admin.FailedLoginCount = 0;
        admin.LockoutEndAt = null;
        admin.LastLoginAt = time.GetUtcNow().UtcDateTime;
    }

    /// <summary>Maintenance: turns two-factor authentication off (lost phone) and revokes the administrator's sessions.</summary>
    public async Task<bool> ResetTwoFactorAsync(string username, SessionService sessions, CancellationToken ct)
    {
        var normalized = Normalize(username);
        var admin = await db.Administrators.FirstOrDefaultAsync(a => a.NormalizedUsername == normalized, ct);
        if (admin is null) return false;
        DisableTwoFactor(admin);
        await db.AdminRecoveryCodes.Where(c => c.AdministratorId == admin.Id).ExecuteDeleteAsync(ct);
        db.Record(ActivityEventType.AdminTwoFactorDisabled, time.GetUtcNow().UtcDateTime, administratorId: admin.Id, detail: "maintenance");
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllForAdministratorAsync(admin.Id, ct);
        return true;
    }

    public static void DisableTwoFactor(Administrator admin)
    {
        admin.TwoFactorEnabled = false;
        admin.TwoFactorSecret = null;
        admin.TwoFactorPendingSecret = null;
        admin.TwoFactorEnabledAt = null;
        admin.TwoFactorLastTimeStep = null;
    }

    private static void EnsurePasswordPolicy(string password)
    {
        if (password.Length < MinPasswordLength)
        {
            throw new AdminConfigurationException($"ADMIN_PASSWORD must be at least {MinPasswordLength} characters long.");
        }
    }
}
