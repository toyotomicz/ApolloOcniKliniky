using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ApolloData.Api.Data;
using ApolloData.Api.Email;
using ApolloData.Api.Geo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace ApolloData.Api.Tests.Infrastructure;

/// <summary>
/// Runs the real API against a disposable PostgreSQL container (Testcontainers).
/// Email delivery and time are substituted; no live SMTP account is ever used.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "Test-Only-Admin-Password-1";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public FakeEmailSender Email { get; } = new();
    public FakeGeocoder Geocoder { get; } = new();
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    protected virtual IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("ADMIN_USERNAME", AdminUsername);
        builder.UseSetting("ADMIN_PASSWORD", AdminPassword);
        builder.UseSetting("COOKIE_SECURE", "true");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("OTP_HASH_KEY", "test-only-otp-key");
        // Generous limits so functional tests are not throttled; RateLimitTests uses its own factory.
        builder.UseSetting("RateLimiting:RegistrationPermits", "1000");
        builder.UseSetting("RateLimiting:OtpRequestPermits", "1000");
        builder.UseSetting("RateLimiting:OtpVerifyPermits", "1000");
        builder.UseSetting("RateLimiting:AdminLoginPermits", "1000");
        builder.UseSetting("RateLimiting:MessagePermits", "1000");
        // The outbox worker runs for real (with the fake sender) and polls quickly.
        builder.UseSetting("EmailOutbox:PollIntervalMilliseconds", "100");
        // The background geocoder would update clinic rows concurrently with the tests (changing their
        // concurrency version); tests drive it explicitly through GeocodingWorker.ProcessNextAsync instead.
        builder.UseSetting("GEOCODING_ENABLED", "false");
        foreach (var (key, value) in ExtraSettings) builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.RemoveAll<IGeocoder>();
            services.AddSingleton<IGeocoder>(Geocoder);
        });
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        // Force host start (runs migrations and admin seeding).
        _ = Server;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>A client with its own cookie jar, talking HTTPS so Secure cookies are sent back.</summary>
    public TestClient CreateTestClient()
    {
        var cookies = new CookieContainer();
        return new TestClient(CreateDefaultClient(TestClient.BaseUri, new CookieContainerHandler(cookies)), cookies);
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

/// <summary>HttpClient wrapper that obtains and sends the CSRF token like the SPA does.</summary>
public sealed class TestClient(HttpClient http, CookieContainer cookies) : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static readonly Uri BaseUri = new("https://localhost");

    private string? _csrf;

    public HttpClient Http => http;

    /// <summary>The raw Cookie header this client would send (used to simulate a stolen/replayed cookie).</summary>
    public string CookieHeader => cookies.GetCookieHeader(BaseUri);

    public async Task<HttpResponseMessage> GetAsync(string url) => await http.GetAsync(url);

    public async Task<HttpResponseMessage> PostAsync(string url, object? body) => await SendAsync(HttpMethod.Post, url, body);

    public async Task<HttpResponseMessage> PutAsync(string url, object? body) => await SendAsync(HttpMethod.Put, url, body);

    /// <summary>The antiforgery token, for requests built by hand.</summary>
    public async Task<string> CsrfTokenAsync() =>
        _csrf ??= (await http.GetFromJsonAsync<JsonElement>("/api/security/csrf")).GetProperty("token").GetString()!;

    public async Task<T> GetJsonAsync<T>(string url)
    {
        var response = await http.GetAsync(url);
        await response.EnsureStatusAsync(System.Net.HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body)
    {
        _csrf ??= (await http.GetFromJsonAsync<JsonElement>("/api/security/csrf")).GetProperty("token").GetString();
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: Json) };
        request.Headers.Add("X-CSRF-TOKEN", _csrf);
        return await http.SendAsync(request);
    }

    public void Dispose() => http.Dispose();
}

public sealed partial class FakeEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];
    private readonly Lock _lock = new();

    public bool Fail { get; set; }

    public IReadOnlyList<EmailMessage> Sent
    {
        get { lock (_lock) return _sent.ToList(); }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Fail) throw new EmailDeliveryException("Simulated SMTP failure.");
        lock (_lock) _sent.Add(message);
        return Task.CompletedTask;
    }

    /// <summary>The latest login code sent to the address.</summary>
    public string? LastCodeFor(string email) => LastCode(email, "přihlašovací kód");

    /// <summary>The latest e-mail verification code of a registration sent to the address.</summary>
    public string? LastRegistrationCodeFor(string email) => LastCode(email, "ověřovací kód pro registraci");

    public int LoginCodeCountFor(string email) => Sent.Count(m => string.Equals(m.ToEmail, email, StringComparison.OrdinalIgnoreCase)
                                                                 && m.Subject.Contains("přihlašovací kód", StringComparison.Ordinal));

    private string? LastCode(string email, string subject)
    {
        var message = Sent.LastOrDefault(m => string.Equals(m.ToEmail, email, StringComparison.OrdinalIgnoreCase)
                                              && m.Subject.Contains(subject, StringComparison.Ordinal));
        return message is null ? null : CodePattern().Match(message.TextBody).Value;
    }

    /// <summary>Waits for an email delivered asynchronously (by the outbox worker).</summary>
    public async Task<EmailMessage> WaitForAsync(Func<EmailMessage, bool> predicate, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (Sent.FirstOrDefault(predicate) is { } message) return message;
            await Task.Delay(50);
        }
        throw new TimeoutException("Expected email was not sent in time.");
    }

    public int CountFor(string email) => Sent.Count(m => string.Equals(m.ToEmail, email, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();
}

/// <summary>Resolves every address to a fixed point unless told otherwise; never calls the network.</summary>
public sealed class FakeGeocoder : IGeocoder
{
    public GeoPoint? Result { get; set; } = new(49.7475, 13.3776);
    public int Calls { get; private set; }

    public Task<GeoPoint?> GeocodeAsync(string street, string postalCode, string city, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

public static class HttpAssertions
{
    public static async Task EnsureStatusAsync(this HttpResponseMessage response, System.Net.HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}: {body}");
        }
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
