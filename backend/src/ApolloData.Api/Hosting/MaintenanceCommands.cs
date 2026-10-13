using ApolloData.Api.Auth;
using ApolloData.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Hosting;

internal static class MaintenanceCommands
{
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
