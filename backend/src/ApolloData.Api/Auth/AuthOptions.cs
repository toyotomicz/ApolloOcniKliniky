namespace ApolloData.Api.Auth;

public static class AuthSchemes
{
    public const string Clinic = "ClinicSession";
    public const string Admin = "AdminSession";
}

public static class AuthPolicies
{
    public const string Clinic = "ClinicOnly";
    public const string Admin = "AdminOnly";
}

public static class AppClaims
{
    public const string SessionId = "sid";
    public const string Subject = "sub";
    public const string Role = "role";
    public const string ClinicRole = "clinic";
    public const string AdminRole = "admin";
}

/// <summary>Authentication settings, bound from the "Auth" configuration section.</summary>
public sealed class AuthOptions
{
    public int ClinicSessionHours { get; set; } = 8;
    public int AdminSessionHours { get; set; } = 8;

    /// <summary>Marks cookies Secure. Set by <c>COOKIE_SECURE</c>; defaults to true outside Development.</summary>
    public bool? SecureCookies { get; set; }

    public int OtpLifetimeMinutes { get; set; } = 10;
    public int OtpMaxAttempts { get; set; } = 5;
    public int OtpResendCooldownSeconds { get; set; } = 60;
    public int OtpMaxCodesPerHour { get; set; } = 5;

    public int AdminMaxFailedAttempts { get; set; } = 5;
    public int AdminLockoutMinutes { get; set; } = 15;
}

/// <summary>Per-IP limits for authentication-sensitive endpoints, bound from "RateLimiting".</summary>
public sealed class RateLimitOptions
{
    public int WindowSeconds { get; set; } = 900;
    public int RegistrationPermits { get; set; } = 10;
    public int OtpRequestPermits { get; set; } = 10;
    public int OtpVerifyPermits { get; set; } = 20;
    public int AdminLoginPermits { get; set; } = 10;
    public int MessagePermits { get; set; } = 30;
}

public static class RateLimitPolicies
{
    public const string Registration = "registration";
    public const string OtpRequest = "otp-request";
    public const string OtpVerify = "otp-verify";
    public const string AdminLogin = "admin-login";
    public const string Messages = "messages";
}
