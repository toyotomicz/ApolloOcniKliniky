using ApolloData.Api.Auth;
using ApolloData.Api.Hosting;

// `dotnet ApolloData.Api.dll --healthcheck` is used by the Docker HEALTHCHECK (the runtime image has no curl).
if (args is ["--healthcheck", ..])
{
    return await HealthProbe.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.AddApolloServices();

var app = builder.Build();

// `dotnet ApolloData.Api.dll reset-admin-password`: local maintenance command, never exposed over HTTP.
// Reads ADMIN_USERNAME / ADMIN_PASSWORD from the environment, creates or resets that administrator,
// clears its lockout and revokes its sessions.
if (args is ["reset-admin-password", ..])
{
    return await MaintenanceCommands.ResetAdminPasswordAsync(app);
}

// `dotnet ApolloData.Api.dll reset-admin-2fa`: turns off two-factor authentication of ADMIN_USERNAME (lost phone).
if (args is ["reset-admin-2fa", ..])
{
    return await MaintenanceCommands.ResetAdminTwoFactorAsync(app);
}

// `dotnet ApolloData.Api.dll seed-demo-data [--remove|--reset]`: optional demo eye clinics for presentations.
if (args is ["seed-demo-data", ..])
{
    return await DemoDataCommand.RunAsync(app, args);
}

try
{
    await app.InitializeDatabaseAsync();
}
catch (AdminConfigurationException ex)
{
    app.Logger.LogCritical("Configuration error: {Message}", ex.Message);
    return 1;
}

app.UseApolloPipeline();
await app.RunAsync();
return 0;

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
