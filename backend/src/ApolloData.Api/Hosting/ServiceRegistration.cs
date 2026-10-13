using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ApolloData.Api.Admin;
using ApolloData.Api.Auth;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using ApolloData.Api.Email;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ApolloData.Api.Hosting;

public static class ServiceRegistration
{
    public const string CorsPolicy = "frontend";
    public const string CsrfHeaderName = "X-CSRF-TOKEN";

    public static WebApplicationBuilder AddApolloServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var isDevelopment = builder.Environment.IsDevelopment();

        if (!isDevelopment)
        {
            // Structured JSON logs outside development.
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
        }

        services.AddSingleton(TimeProvider.System);

        // --- Persistence ---------------------------------------------------------------------
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'ConnectionStrings:Default' is not configured (env ConnectionStrings__Default).");
            }
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3));
        });

        // --- Options -------------------------------------------------------------------------
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection("Auth"))
            .PostConfigure(o =>
            {
                if (bool.TryParse(configuration["COOKIE_SECURE"], out var secure)) o.SecureCookies = secure;
                o.SecureCookies ??= !isDevelopment;
            });
        services.AddOptions<RateLimitOptions>().Bind(configuration.GetSection("RateLimiting"));
        services.AddOptions<SmtpOptions>().Configure<IConfiguration>(SmtpOptions.Bind);

        // --- Application services --------------------------------------------------------------
        services.AddSingleton<OtpCodeHasher>();
        services.AddScoped<ClinicOtpService>();
        services.AddScoped<SessionService>();
        services.AddScoped<AdministratorAccounts>();
        services.AddScoped<DashboardService>();
        services.AddSingleton<ReportingClock>();
        services.AddSingleton<IPasswordHasher<Administrator>, PasswordHasher<Administrator>>();
        services.AddSingleton<IEmailSender, MailKitEmailSender>();

        // --- HTTP, JSON, errors ------------------------------------------------------------------
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
        services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddOpenApi();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // --- Data protection (cookie encryption keys) -----------------------------------------------
        var dataProtection = services.AddDataProtection().SetApplicationName("ApolloData");
        if (configuration["DATA_PROTECTION_KEYS_PATH"] is { Length: > 0 } keysPath)
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        // --- Authentication: two independent cookie schemes backed by server-side sessions ---------
        services.AddAuthentication()
            .AddCookie(AuthSchemes.Clinic, o => ConfigureCookie(o, "apollo_clinic", SessionKind.Clinic))
            .AddCookie(AuthSchemes.Admin, o => ConfigureCookie(o, "apollo_admin", SessionKind.Administrator));
        services.AddOptions<CookieAuthenticationOptions>(AuthSchemes.Clinic).Configure<IOptions<AuthOptions>>((o, a) => ApplyCookieSecurity(o, a.Value, a.Value.ClinicSessionHours));
        services.AddOptions<CookieAuthenticationOptions>(AuthSchemes.Admin).Configure<IOptions<AuthOptions>>((o, a) => ApplyCookieSecurity(o, a.Value, a.Value.AdminSessionHours));

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Clinic, p => p
                .AddAuthenticationSchemes(AuthSchemes.Clinic)
                .RequireAuthenticatedUser()
                .RequireClaim(AppClaims.Role, AppClaims.ClinicRole))
            .AddPolicy(AuthPolicies.Admin, p => p
                .AddAuthenticationSchemes(AuthSchemes.Admin)
                .RequireAuthenticatedUser()
                .RequireClaim(AppClaims.Role, AppClaims.AdminRole));

        services.AddAntiforgery(o =>
        {
            o.HeaderName = CsrfHeaderName;
            o.Cookie.Name = "apollo_csrf";
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.HttpOnly = true;
            // Antiforgery refuses to work at all over plain HTTP with SecurePolicy=Always, which would break
            // http://localhost. Behind TLS (directly or via X-Forwarded-Proto) the cookie is still marked Secure.
            // This cookie only holds the CSRF token half, not a session.
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        // --- Rate limiting (per client IP) --------------------------------------------------------
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Příliš mnoho požadavků.",
                    Detail = "Z vaší adresy přišlo příliš mnoho požadavků. Zkuste to prosím později.",
                };
                await context.HttpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null,
                    "application/problem+json", ct);
            };
            AddIpPolicy(o, RateLimitPolicies.Registration, l => l.RegistrationPermits);
            AddIpPolicy(o, RateLimitPolicies.OtpRequest, l => l.OtpRequestPermits);
            AddIpPolicy(o, RateLimitPolicies.OtpVerify, l => l.OtpVerifyPermits);
            AddIpPolicy(o, RateLimitPolicies.AdminLogin, l => l.AdminLoginPermits);
        });

        // --- CORS: only explicitly configured origins (not needed when the SPA is served same-origin) --
        var origins = (configuration["CORS_ALLOWED_ORIGINS"] ?? "")
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
        {
            if (origins.Length > 0)
            {
                p.WithOrigins(origins).AllowCredentials()
                    .WithHeaders("Content-Type", CsrfHeaderName)
                    .WithMethods("GET", "POST", "PUT", "DELETE");
            }
        }));

        // --- Reverse proxy -----------------------------------------------------------------------
        if (bool.TryParse(configuration["TRUST_FORWARDED_HEADERS"], out var trustProxy) && trustProxy)
        {
            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.ForwardLimit = 1;
                // The API container is reachable only through the Nginx container on the internal network.
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            });
        }

        return builder;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions o, string cookieName, SessionKind kind)
    {
        o.Cookie.Name = cookieName;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.Path = "/";
        o.SlidingExpiration = false;
        o.Events = new CookieAuthenticationEvents
        {
            // APIs answer with status codes instead of redirecting to a login page.
            OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; },
            OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; },
            OnValidatePrincipal = ctx => SessionService.ValidatePrincipalAsync(ctx, kind),
        };
    }

    private static void ApplyCookieSecurity(CookieAuthenticationOptions o, AuthOptions auth, int sessionHours)
    {
        o.Cookie.SecurePolicy = auth.SecureCookies == true ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(sessionHours);
    }

    private static void AddIpPolicy(RateLimiterOptions options, string name, Func<RateLimitOptions, int> permits) =>
        options.AddPolicy(name, http =>
        {
            var limits = http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
            var ip = http.Connection.RemoteIpAddress ?? IPAddress.None;
            return RateLimitPartition.GetFixedWindowLimiter($"{name}:{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits(limits),
                Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                QueueLimit = 0,
            });
        });
}

internal sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database is not reachable.");
}
