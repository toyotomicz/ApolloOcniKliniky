using ApolloData.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ApolloData.Api.Realtime;

/// <summary>
/// Server-to-client notifications for signed-in administrators (new registrations, clinic messages, data changes).
/// Clients only listen: the hub exposes no methods that a browser could invoke, so it cannot change any data.
/// Authentication uses the same admin session cookie as the REST API (validated against the database).
/// </summary>
[Authorize(Policy = AuthPolicies.Admin)]
public sealed class AdminHub : Hub
{
    public const string Path = "/hubs/admin";
}

public static class AdminEvents
{
    public const string RegistrationCreated = "registrationCreated";
    public const string MessageReceived = "messageReceived";
    public const string ClinicsChanged = "clinicsChanged";
}

public sealed record RegistrationCreatedEvent(Guid ClinicId, string ClinicName, string PlanCode, string City, DateTime CreatedAt);

public sealed record MessageReceivedEvent(Guid ClinicId, string ClinicName, DateTime CreatedAt);

/// <summary>Something about these clinics changed (status, profile, verification); open views may reload.</summary>
public sealed record ClinicsChangedEvent(IReadOnlyList<Guid> ClinicIds, string Reason);

public interface IAdminNotifier
{
    Task RegistrationCreatedAsync(RegistrationCreatedEvent e);
    Task MessageReceivedAsync(MessageReceivedEvent e);
    Task ClinicsChangedAsync(ClinicsChangedEvent e);
}

/// <summary>
/// Broadcasts to all connected administrators. Notifications are best-effort: a failure is logged and never
/// fails the request that triggered it (the data is already committed and visible after a reload).
/// </summary>
internal sealed class SignalRAdminNotifier(IHubContext<AdminHub> hub, ILogger<SignalRAdminNotifier> logger) : IAdminNotifier
{
    public Task RegistrationCreatedAsync(RegistrationCreatedEvent e) => SendAsync(AdminEvents.RegistrationCreated, e);

    public Task MessageReceivedAsync(MessageReceivedEvent e) => SendAsync(AdminEvents.MessageReceived, e);

    public Task ClinicsChangedAsync(ClinicsChangedEvent e) => SendAsync(AdminEvents.ClinicsChanged, e);

    private async Task SendAsync(string method, object payload)
    {
        try
        {
            await hub.Clients.All.SendAsync(method, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime notification {Method} could not be sent", method);
        }
    }
}
