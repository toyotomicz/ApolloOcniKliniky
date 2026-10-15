using System.Security.Cryptography;
using System.Text;
using ApolloData.Api.Common;
using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using QRCoder;

namespace ApolloData.Api.Auth;

/// <summary>
/// TOTP two-factor authentication for administrators (RFC 6238: SHA-1, 6 digits, 30 s – the defaults of
/// Google Authenticator, Microsoft Authenticator and others).
/// <list type="bullet">
/// <item>Secrets are encrypted at rest with ASP.NET Core Data Protection (keys in the persistent key ring).</item>
/// <item>A code is accepted within ±1 time step for clock drift, and never twice (last used step is stored).</item>
/// <item>Recovery codes are random, single-use and stored as SHA-256 hashes (they carry ~50 bits of entropy).</item>
/// </list>
/// </summary>
public sealed class TwoFactorService(IDataProtectionProvider dataProtection, TimeProvider time, AppInfo app)
{
    public const int RecoveryCodeCount = 10;

    // Unambiguous alphabet (no 0/O, 1/I/L) for codes that people copy by hand.
    private const string RecoveryAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private readonly IDataProtector _secrets = dataProtection.CreateProtector("ApolloData.Admin.TotpSecret.v1");

    public sealed record Secret(string Base32, string Protected);

    public Secret CreateSecret()
    {
        var base32 = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        return new Secret(base32, _secrets.Protect(base32));
    }

    public string Unprotect(string protectedSecret) => _secrets.Unprotect(protectedSecret);

    /// <summary>Base32 secret in groups of four for manual entry, e.g. "ABCD EFGH ...".</summary>
    public static string FormatForDisplay(string base32) =>
        string.Join(' ', Enumerable.Range(0, (base32.Length + 3) / 4).Select(i => base32.Substring(i * 4, Math.Min(4, base32.Length - i * 4))));

    public string OtpAuthUri(string username, string base32)
    {
        var issuer = Uri.EscapeDataString(app.Name);
        return $"otpauth://totp/{issuer}:{Uri.EscapeDataString(username)}?secret={base32}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";
    }

    /// <summary>PNG QR code as a data URI (allowed by the CSP's img-src data:), generated on the server.</summary>
    public static string QrCodeDataUri(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(6);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }

    /// <summary>Returns the matched time step when the code is valid and newer than <paramref name="lastUsedStep"/>.</summary>
    public long? VerifyCode(string base32Secret, string? code, long? lastUsedStep)
    {
        var normalized = code?.Replace(" ", "").Trim();
        if (normalized is not { Length: 6 } || !normalized.All(char.IsAsciiDigit)) return null;

        var totp = new Totp(Base32Encoding.ToBytes(base32Secret));
        var now = time.GetUtcNow().UtcDateTime;
        if (!totp.VerifyTotp(now, normalized, out var step, new VerificationWindow(previous: 1, future: 1))) return null;
        return lastUsedStep is { } last && step <= last ? null : step;
    }

    public static IReadOnlyList<string> CreateRecoveryCodes() =>
        Enumerable.Range(0, RecoveryCodeCount).Select(_ =>
        {
            var chars = RandomNumberGenerator.GetItems<char>(RecoveryAlphabet, 10);
            return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
        }).ToList();

    public static byte[] HashRecoveryCode(string code)
    {
        var normalized = code.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
        return SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
    }
}

/// <summary>
/// Short-lived proof that the password step succeeded, kept in an encrypted HttpOnly cookie until the
/// second factor is verified. Expires after 5 minutes; no session exists before both factors pass.
/// </summary>
public sealed class PendingTwoFactorLogin(IDataProtectionProvider dataProtection, TimeProvider time)
{
    public const string CookieName = "apollo_admin_2fa";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ITimeLimitedDataProtector _protector =
        dataProtection.CreateProtector("ApolloData.Admin.PendingTwoFactor.v1").ToTimeLimitedDataProtector();

    public void Issue(HttpContext http, Guid administratorId, bool secureCookies)
    {
        var expires = time.GetUtcNow().Add(Lifetime);
        var value = _protector.Protect(administratorId.ToString("N"), expires);
        http.Response.Cookies.Append(CookieName, value, Options(http, secureCookies, expires));
    }

    public Guid? Read(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var value)) return null;
        try
        {
            var payload = _protector.Unprotect(value, out var expiration);
            return expiration > time.GetUtcNow() && Guid.TryParseExact(payload, "N", out var id) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static void Clear(HttpContext http, bool secureCookies) =>
        http.Response.Cookies.Delete(CookieName, Options(http, secureCookies, null));

    private static CookieOptions Options(HttpContext http, bool secureCookies, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = secureCookies || http.Request.IsHttps,
        Path = "/api/admin-auth",
        Expires = expires,
        IsEssential = true,
    };
}
