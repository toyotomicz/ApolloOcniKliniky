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
        await db.SaveChangesAsync(ct);

        if (!created) await sessions.RevokeAllForAdministratorAsync(admin.Id, ct);
        return created;
    }

    public async Task<(AdminLoginResult Result, Administrator? Admin)> VerifyLoginAsync(string username, string password, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var normalized = Normalize(username);
        var admin = await db.Administrators.FirstOrDefaultAsync(a => a.NormalizedUsername == normalized, ct);

        if (admin is null || !admin.IsActive)
        {
            _dummyHash ??= hasher.HashPassword(DummyAdmin, Guid.NewGuid().ToString());
            hasher.VerifyHashedPassword(DummyAdmin, _dummyHash, password);
            logger.LogInformation("Administrator login failed: unknown or inactive account");
            return (AdminLoginResult.Failed, null);
        }

        if (admin.LockoutEndAt > now)
        {
            logger.LogWarning("Administrator login rejected: account {AdminId} locked until {LockoutEnd:o}", admin.Id, admin.LockoutEndAt);
            return (AdminLoginResult.Failed, null);
        }

        var verification = hasher.VerifyHashedPassword(admin, admin.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            admin.FailedLoginCount++;
            if (admin.FailedLoginCount >= options.Value.AdminMaxFailedAttempts)
            {
                admin.LockoutEndAt = now.AddMinutes(options.Value.AdminLockoutMinutes);
                admin.FailedLoginCount = 0;
                logger.LogWarning("Administrator account {AdminId} locked after repeated failed logins", admin.Id);
            }
            await db.SaveChangesAsync(ct);
            return (AdminLoginResult.Failed, null);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            admin.PasswordHash = hasher.HashPassword(admin, password);
        }
        admin.FailedLoginCount = 0;
        admin.LockoutEndAt = null;
        admin.LastLoginAt = now;
        await db.SaveChangesAsync(ct);
        return (AdminLoginResult.Success, admin);
    }

    private static void EnsurePasswordPolicy(string password)
    {
        if (password.Length < MinPasswordLength)
        {
            throw new AdminConfigurationException($"ADMIN_PASSWORD must be at least {MinPasswordLength} characters long.");
        }
    }
}
