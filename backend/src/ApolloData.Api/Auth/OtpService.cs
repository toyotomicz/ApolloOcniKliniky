using System.Security.Cryptography;
using System.Text;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using ApolloData.Api.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApolloData.Api.Auth;

/// <summary>
/// Generates and hashes one-time codes. Codes are hashed with HMAC-SHA256 using a server-side key,
/// so a database dump alone is not enough to brute-force the 6-digit space offline.
/// The key comes from <c>OTP_HASH_KEY</c>; if not set, a random per-process key is used
/// (pending codes then simply become invalid after a restart, which is acceptable for 10-minute codes).
/// </summary>
public sealed class OtpCodeHasher
{
    private readonly byte[] _key;

    public OtpCodeHasher(IConfiguration configuration, ILogger<OtpCodeHasher> logger)
    {
        var configured = configuration["OTP_HASH_KEY"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            _key = RandomNumberGenerator.GetBytes(32);
            logger.LogInformation("OTP_HASH_KEY not set; using an ephemeral per-process key for one-time code hashing");
        }
        else
        {
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        }
    }

    internal OtpCodeHasher(byte[] key) => _key = key;

    public static string GenerateCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>Binds the hash to the challenge id so equal codes never produce equal hashes.</summary>
    public byte[] Hash(Guid challengeId, string code)
    {
        var payload = Encoding.UTF8.GetBytes($"{challengeId:N}:{code}");
        return HMACSHA256.HashData(_key, payload);
    }

    public bool Verify(Guid challengeId, string code, byte[] expectedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(challengeId, code), expectedHash);
}

public enum CodeIssueResult
{
    Sent,
    Throttled,
    DeliveryFailed,
}

public enum CodeVerifyResult
{
    Success,
    Invalid,
}

public sealed class ClinicOtpService(
    AppDbContext db,
    OtpCodeHasher hasher,
    IEmailSender emailSender,
    TimeProvider time,
    IOptions<AuthOptions> authOptions,
    IConfiguration configuration,
    ILogger<ClinicOtpService> logger)
{
    private AuthOptions Options => authOptions.Value;

    public async Task<CodeIssueResult> IssueAsync(Clinic clinic, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;

        // Per-account throttling (in addition to per-IP rate limiting). Only delivered codes count,
        // so an SMTP outage does not lock the clinic out of retrying.
        var hourAgo = now.AddHours(-1);
        var recent = await db.ClinicLoginChallenges
            .Where(c => c.ClinicId == clinic.Id && c.DeliveredAt != null && c.CreatedAt > hourAgo)
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);

        if (recent.Count >= Options.OtpMaxCodesPerHour
            || recent.Any(t => t > now.AddSeconds(-Options.OtpResendCooldownSeconds)))
        {
            logger.LogInformation("Login code request for clinic {ClinicId} throttled", clinic.Id);
            return CodeIssueResult.Throttled;
        }

        var code = OtpCodeHasher.GenerateCode();
        var challenge = new ClinicLoginChallenge
        {
            Id = Guid.CreateVersion7(),
            ClinicId = clinic.Id,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(Options.OtpLifetimeMinutes),
            CodeHash = [],
        };
        challenge.CodeHash = hasher.Hash(challenge.Id, code);
        db.ClinicLoginChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);

        var appName = configuration["APP_NAME"] ?? "ApolloData";
        var message = EmailTemplates.LoginCode(appName, clinic.Email,
            $"{clinic.ContactFirstName} {clinic.ContactLastName}".Trim(), code, Options.OtpLifetimeMinutes);

        try
        {
            await emailSender.SendAsync(message, ct);
        }
        catch (EmailDeliveryException)
        {
            // The undelivered code can never be used; the previous code (if any) stays valid.
            await db.ClinicLoginChallenges
                .Where(c => c.Id == challenge.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.InvalidatedAt, now), CancellationToken.None);
            logger.LogWarning("Login code for clinic {ClinicId} could not be delivered", clinic.Id);
            return CodeIssueResult.DeliveryFailed;
        }

        var deliveredAt = time.GetUtcNow().UtcDateTime;
        await db.ClinicLoginChallenges
            .Where(c => c.Id == challenge.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeliveredAt, deliveredAt), CancellationToken.None);

        // A newly delivered code supersedes all older, still-open codes of the clinic.
        await db.ClinicLoginChallenges
            .Where(c => c.ClinicId == clinic.Id && c.Id != challenge.Id && c.CreatedAt <= challenge.CreatedAt
                        && c.ConsumedAt == null && c.InvalidatedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.InvalidatedAt, deliveredAt), CancellationToken.None);

        logger.LogInformation("Login code issued for clinic {ClinicId}, expires at {ExpiresAt:o}", clinic.Id, challenge.ExpiresAt);
        return CodeIssueResult.Sent;
    }

    /// <summary>
    /// Verifies a code. Attempt counting and consumption are single conditional UPDATE statements,
    /// so concurrent requests cannot exceed the attempt limit or consume the same code twice.
    /// </summary>
    public async Task<CodeVerifyResult> VerifyAsync(Guid clinicId, string code, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var maxAttempts = Options.OtpMaxAttempts;

        var challenge = await db.ClinicLoginChallenges
            .AsNoTracking()
            .Where(c => c.ClinicId == clinicId && c.DeliveredAt != null && c.ConsumedAt == null
                        && c.InvalidatedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (challenge is null)
        {
            logger.LogInformation("Login code verification for clinic {ClinicId}: no active code", clinicId);
            return CodeVerifyResult.Invalid;
        }

        var reserved = await db.ClinicLoginChallenges
            .Where(c => c.Id == challenge.Id && c.AttemptCount < maxAttempts && c.ConsumedAt == null && c.InvalidatedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.AttemptCount, c => c.AttemptCount + 1), ct);

        if (reserved == 0)
        {
            logger.LogInformation("Login code verification for clinic {ClinicId}: attempts exhausted", clinicId);
            return CodeVerifyResult.Invalid;
        }

        if (!hasher.Verify(challenge.Id, code, challenge.CodeHash))
        {
            logger.LogInformation("Login code verification for clinic {ClinicId}: wrong code (attempt {Attempt}/{Max})",
                clinicId, challenge.AttemptCount + 1, maxAttempts);
            return CodeVerifyResult.Invalid;
        }

        var consumed = await db.ClinicLoginChallenges
            .Where(c => c.Id == challenge.Id && c.ConsumedAt == null && c.InvalidatedAt == null && c.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);

        return consumed == 1 ? CodeVerifyResult.Success : CodeVerifyResult.Invalid;
    }
}
