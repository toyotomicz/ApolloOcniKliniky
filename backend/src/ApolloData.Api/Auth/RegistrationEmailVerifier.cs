using System.Security.Cryptography;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using ApolloData.Api.Email;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApolloData.Api.Auth;

public sealed record RegistrationVerificationToken(string Token, DateTime ExpiresAt);

/// <summary>
/// E-mail verification at the start of a registration, before the clinic exists. Mirrors the login code
/// rules of <see cref="ClinicOtpService"/> (lifetime, attempts, cooldown, hourly limit); a confirmed code is
/// exchanged for a one-time token (<c>{id}.{secret}</c>) that <c>POST /api/registrations</c> requires.
/// </summary>
public sealed class RegistrationEmailVerifier(
    AppDbContext db,
    OtpCodeHasher hasher,
    IEmailSender emailSender,
    TimeProvider time,
    IOptions<AuthOptions> authOptions,
    AppInfo app,
    ILogger<RegistrationEmailVerifier> logger)
{
    /// <summary>How long the rest of the form may take after the e-mail was verified.</summary>
    public const int TokenLifetimeMinutes = 120;

    private AuthOptions Options => authOptions.Value;

    public async Task<CodeIssueResult> IssueAsync(string email, string? recipientName, CancellationToken ct)
    {
        var normalized = Rules.NormalizeEmail(email);
        var now = time.GetUtcNow().UtcDateTime;

        var hourAgo = now.AddHours(-1);
        var recent = await db.RegistrationEmailVerifications
            .Where(v => v.NormalizedEmail == normalized && v.DeliveredAt != null && v.CreatedAt > hourAgo)
            .Select(v => v.CreatedAt)
            .ToListAsync(ct);
        if (recent.Count >= Options.OtpMaxCodesPerHour
            || recent.Any(t => t > now.AddSeconds(-Options.OtpResendCooldownSeconds)))
        {
            logger.LogInformation("Registration code request throttled");
            return CodeIssueResult.Throttled;
        }

        var code = OtpCodeHasher.GenerateCode();
        var verification = new RegistrationEmailVerification
        {
            Id = Guid.CreateVersion7(),
            NormalizedEmail = normalized,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(Options.OtpLifetimeMinutes),
            CodeHash = [],
        };
        verification.CodeHash = hasher.Hash(verification.Id, code);
        db.RegistrationEmailVerifications.Add(verification);
        await db.SaveChangesAsync(ct);

        try
        {
            await emailSender.SendAsync(
                EmailTemplates.RegistrationCode(app.Name, email.Trim(), recipientName, code, Options.OtpLifetimeMinutes), ct);
        }
        catch (EmailDeliveryException)
        {
            await db.RegistrationEmailVerifications
                .Where(v => v.Id == verification.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.InvalidatedAt, now), CancellationToken.None);
            logger.LogWarning("Registration code {VerificationId} could not be delivered", verification.Id);
            return CodeIssueResult.DeliveryFailed;
        }

        var deliveredAt = time.GetUtcNow().UtcDateTime;
        await db.RegistrationEmailVerifications
            .Where(v => v.Id == verification.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.DeliveredAt, deliveredAt), CancellationToken.None);

        // The newest delivered code supersedes older open codes for the same address.
        await db.RegistrationEmailVerifications
            .Where(v => v.NormalizedEmail == normalized && v.Id != verification.Id && v.CreatedAt <= verification.CreatedAt
                        && v.VerifiedAt == null && v.InvalidatedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.InvalidatedAt, deliveredAt), CancellationToken.None);

        logger.LogInformation("Registration code {VerificationId} issued, expires at {ExpiresAt:o}", verification.Id, verification.ExpiresAt);
        return CodeIssueResult.Sent;
    }

    /// <summary>
    /// Checks the code (attempts are counted with a conditional UPDATE, as for login codes) and, when it matches,
    /// marks the verification as done and returns a new registration token. Returns null for a wrong or expired code.
    /// </summary>
    public async Task<RegistrationVerificationToken?> VerifyAsync(string email, string code, CancellationToken ct)
    {
        var normalized = Rules.NormalizeEmail(email);
        var now = time.GetUtcNow().UtcDateTime;
        var maxAttempts = Options.OtpMaxAttempts;

        var verification = await db.RegistrationEmailVerifications
            .AsNoTracking()
            .Where(v => v.NormalizedEmail == normalized && v.DeliveredAt != null && v.VerifiedAt == null
                        && v.InvalidatedAt == null && v.ExpiresAt > now)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (verification is null) return null;

        var reserved = await db.RegistrationEmailVerifications
            .Where(v => v.Id == verification.Id && v.AttemptCount < maxAttempts && v.VerifiedAt == null && v.InvalidatedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.AttemptCount, v => v.AttemptCount + 1), ct);
        if (reserved == 0 || !hasher.Verify(verification.Id, code, verification.CodeHash))
        {
            logger.LogInformation("Registration code {VerificationId}: wrong code or attempts exhausted", verification.Id);
            return null;
        }

        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var tokenHash = hasher.Hash(verification.Id, secret);
        var expiresAt = now.AddMinutes(TokenLifetimeMinutes);
        var verified = await db.RegistrationEmailVerifications
            .Where(v => v.Id == verification.Id && v.VerifiedAt == null && v.InvalidatedAt == null && v.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.VerifiedAt, now)
                .SetProperty(v => v.TokenHash, tokenHash)
                .SetProperty(v => v.TokenExpiresAt, expiresAt), ct);
        if (verified != 1) return null;

        logger.LogInformation("Registration e-mail verified ({VerificationId})", verification.Id);
        return new RegistrationVerificationToken($"{verification.Id:N}.{secret}", expiresAt);
    }

    /// <summary>
    /// Finds the verification that belongs to a still valid, unused token for the given address. The entity is
    /// tracked, so the caller marks it consumed in the same SaveChanges that creates the registration.
    /// </summary>
    public async Task<RegistrationEmailVerification?> FindVerifiedAsync(string email, string? token, CancellationToken ct)
    {
        var parts = token?.Split('.', 2);
        if (parts is not [var idText, { Length: > 0 } secret] || !Guid.TryParseExact(idText, "N", out var id)) return null;

        var normalized = Rules.NormalizeEmail(email);
        var now = time.GetUtcNow().UtcDateTime;
        var verification = await db.RegistrationEmailVerifications.FirstOrDefaultAsync(
            v => v.Id == id && v.NormalizedEmail == normalized && v.VerifiedAt != null && v.ConsumedAt == null && v.TokenExpiresAt > now, ct);

        return verification?.TokenHash is { } hash && CryptographicOperations.FixedTimeEquals(hasher.Hash(id, secret), hash)
            ? verification
            : null;
    }
}
