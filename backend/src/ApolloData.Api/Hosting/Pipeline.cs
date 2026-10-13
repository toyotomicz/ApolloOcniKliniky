using ApolloData.Api.Admin;
using ApolloData.Api.Auth;
using ApolloData.Api.Clinics;
using ApolloData.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApolloData.Api.Hosting;

public static class Pipeline
{
    public static WebApplication UseApolloPipeline(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("TRUST_FORWARDED_HEADERS"))
        {
            app.UseForwardedHeaders();
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                // Personal data must not be cached by browsers or proxies.
                headers.CacheControl = "no-cache, no-store";
                headers.Pragma = "no-cache";
            }
            await next();
        });

        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("ENABLE_SWAGGER"))
        {
            app.MapOpenApi();
            app.UseSwaggerUI(o =>
            {
                o.SwaggerEndpoint("/openapi/v1.json", "ApolloData API v1");
                o.RoutePrefix = "swagger";
            });
        }

        app.UseCors(ServiceRegistration.CorsPolicy);
        app.UseAuthentication();
        app.UseMiddleware<CsrfValidationMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

        app.MapPublicEndpoints();
        app.MapClinicAuthEndpoints();
        app.MapClinicPortalEndpoints();
        app.MapAdminAuthEndpoints();
        app.MapAdminEndpoints();

        return app;
    }

    /// <summary>Applies pending EF Core migrations and seeds the initial administrator.</summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

        if (app.Configuration.GetValue("DATABASE_MIGRATE_ON_STARTUP", true))
        {
            // Dedicated context for migrations: on an empty database EF first probes the not-yet-existing
            // __EFMigrationsHistory table, which it would log as an Error. That probe is expected, so command
            // errors are logged at Debug here only; a real migration failure still throws and stops startup.
            var connectionString = services.GetRequiredService<AppDbContext>().Database.GetConnectionString();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .UseLoggerFactory(services.GetRequiredService<ILoggerFactory>())
                .ConfigureWarnings(w => w.Log((RelationalEventId.CommandError, LogLevel.Debug)))
                .Options;
            await using var db = new AppDbContext(options);

            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} database migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            }
            await db.Database.MigrateAsync();
        }

        var accounts = services.GetRequiredService<AdministratorAccounts>();
        await accounts.SeedAsync(app.Configuration["ADMIN_USERNAME"], app.Configuration["ADMIN_PASSWORD"], CancellationToken.None);
    }
}

/// <summary>
/// CSRF protection for the cookie-authenticated JSON API: every unsafe request to /api must carry the
/// antiforgery request token (from GET /api/security/csrf) in the X-CSRF-TOKEN header.
/// This is in addition to SameSite=Strict cookies and the narrow CORS policy.
/// </summary>
internal sealed class CsrfValidationMiddleware(RequestDelegate next, IAntiforgery antiforgery, ILogger<CsrfValidationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method))
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                logger.LogInformation("Rejected {Method} {Path}: missing or invalid CSRF token", context.Request.Method, context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Neplatný bezpečnostní token.",
                    Detail = "Platnost formuláře vypršela. Obnovte prosím stránku a akci opakujte.",
                    Extensions = { ["code"] = "csrf_invalid" },
                }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
                return;
            }
        }
        await next(context);
    }
}
