using ApolloData.Api.Auth;
using ApolloData.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Hosting;

internal static class MaintenanceCommands
{
    /// <summary>Turns off two-factor authentication of ADMIN_USERNAME (e.g. after losing the phone) and revokes its sessions.</summary>
    public static async Task<int> ResetAdminTwoFactorAsync(WebApplication app)
    {
        var username = app.Configuration["ADMIN_USERNAME"];
        if (string.IsNullOrWhiteSpace(username))
        {
            Console.Error.WriteLine("ADMIN_USERNAME must be set in the environment.");
            return 2;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        var found = await services.GetRequiredService<AdministratorAccounts>()
            .ResetTwoFactorAsync(username, services.GetRequiredService<SessionService>(), CancellationToken.None);
        Console.WriteLine(found
            ? $"Two-factor authentication of administrator '{username}' was turned off; existing sessions were revoked."
            : $"Administrator '{username}' does not exist.");
        return found ? 0 : 2;
    }

    public static async Task<int> ResetAdminPasswordAsync(WebApplication app)
    {
        var username = app.Configuration["ADMIN_USERNAME"];
        var password = app.Configuration["ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("ADMIN_USERNAME and ADMIN_PASSWORD must be set in the environment.");
            return 2;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        try
        {
            var created = await services.GetRequiredService<AdministratorAccounts>()
                .ResetPasswordAsync(username, password, services.GetRequiredService<SessionService>(), CancellationToken.None);
            Console.WriteLine(created
                ? $"Administrator '{username}' created."
                : $"Password for administrator '{username}' was reset; existing sessions were revoked.");
            return 0;
        }
        catch (AdminConfigurationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }
}

internal static class DemoDataCommand
{
    /// <summary><c>seed-demo-data</c> [--remove | --reset]</summary>
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        await app.InitializeDatabaseAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        var remove = args.Contains("--remove");
        var reset = args.Contains("--reset");

        var existing = await seeder.CountAsync(CancellationToken.None);
        if (remove || reset)
        {
            var removed = await seeder.RemoveAsync(CancellationToken.None);
            Console.WriteLine($"Removed {removed} demo clinic(s).");
            if (remove) return 0;
        }
        else if (existing > 0)
        {
            Console.WriteLine($"Demo data already present ({existing} clinics). Use --reset to recreate it or --remove to delete it.");
            return 0;
        }

        var created = await seeder.SeedAsync(CancellationToken.None);
        Console.WriteLine($"Created {created} demo eye clinics (emails @{DemoDataSeeder.EmailDomain}, never delivered).");
        return 0;
    }
}

internal static class HealthProbe
{
    public static async Task<int> RunAsync()
    {
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';', ',')[0];
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await client.GetAsync($"http://127.0.0.1:{port}/health/ready");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
