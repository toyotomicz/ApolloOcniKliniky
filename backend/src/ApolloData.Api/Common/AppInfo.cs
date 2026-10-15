namespace ApolloData.Api.Common;

/// <summary>Application name and public address, used in emails, PDFs and the authenticator app entry.</summary>
public sealed class AppInfo(IConfiguration configuration)
{
    public string Name { get; } = configuration["APP_NAME"] is { Length: > 0 } name ? name : "ApolloData";

    /// <summary>Public URL of the web application (APP_PUBLIC_URL), without a trailing slash.</summary>
    public string PublicUrl { get; } = (configuration["APP_PUBLIC_URL"] is { Length: > 0 } url ? url : "http://localhost:8080").TrimEnd('/');

    public string ClinicLoginUrl => $"{PublicUrl}/prihlaseni";
}

/// <summary>
/// Coalescing wake-up signal for a background worker: producers call <see cref="Notify"/> after committing work,
/// the worker waits on it with a fallback timeout. Subclassed so each worker gets its own DI singleton.
/// </summary>
public abstract class WorkSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        if (_signal.CurrentCount != 0) return;
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Another producer won the race; one pending wake-up is enough.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}
