using System.Globalization;
using System.Text.Json;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApolloData.Api.Geo;

/// <summary>
/// Settings for resolving clinic addresses to map coordinates (env GEOCODING_ENABLED, GEOCODING_URL,
/// GEOCODING_CONTACT_EMAIL). Uses OpenStreetMap Nominatim by default, which sends the clinic's street,
/// postal code and city to that service. Turn it off with GEOCODING_ENABLED=false.
/// </summary>
public sealed class GeocodingOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

    /// <summary>Optional contact passed to Nominatim as recommended by its usage policy.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>Pause between requests. Nominatim's public instance allows at most one request per second.</summary>
    public int RequestIntervalMilliseconds { get; set; } = 1100;

    /// <summary>Addresses that could not be resolved are tried again after this many hours.</summary>
    public int RetryAfterHours { get; set; } = 24;

    public int IdlePollSeconds { get; set; } = 60;

    public static void Bind(GeocodingOptions options, IConfiguration configuration)
    {
        configuration.GetSection("Geocoding").Bind(options);
        if (bool.TryParse(configuration["GEOCODING_ENABLED"], out var enabled)) options.Enabled = enabled;
        if (configuration["GEOCODING_URL"] is { Length: > 0 } url) options.BaseUrl = url;
        if (configuration["GEOCODING_CONTACT_EMAIL"] is { Length: > 0 } email) options.ContactEmail = email;
    }
}

public sealed record GeoPoint(double Latitude, double Longitude);

public interface IGeocoder
{
    /// <summary>Resolves a Czech address; null when nothing was found.</summary>
    Task<GeoPoint?> GeocodeAsync(string street, string postalCode, string city, CancellationToken ct);
}

public sealed class GeocodingSignal : WorkSignal;

/// <summary>Structured search against Nominatim, falling back to postal code + city when the street is not found.</summary>
internal sealed class NominatimGeocoder(HttpClient http, IOptions<GeocodingOptions> options) : IGeocoder
{
    public async Task<GeoPoint?> GeocodeAsync(string street, string postalCode, string city, CancellationToken ct) =>
        await SearchAsync(new() { ["street"] = street, ["postalcode"] = postalCode.Replace(" ", ""), ["city"] = city }, ct)
        ?? await SearchAsync(new() { ["postalcode"] = postalCode.Replace(" ", ""), ["city"] = city }, ct);

    private async Task<GeoPoint?> SearchAsync(Dictionary<string, string> parameters, CancellationToken ct)
    {
        parameters["countrycodes"] = "cz";
        parameters["format"] = "jsonv2";
        parameters["limit"] = "1";
        if (options.Value.ContactEmail is { Length: > 0 } email) parameters["email"] = email;

        var query = string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        using var response = await http.GetAsync($"search?{query}", ct);
        response.EnsureSuccessStatusCode();

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0) return null;

        var first = json.RootElement[0];
        return double.TryParse(first.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
               && double.TryParse(first.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
            ? new GeoPoint(lat, lon)
            : null;
    }
}

/// <summary>
/// Resolves clinics without coordinates one at a time (newest first), respecting the request interval.
/// Woken by <see cref="GeocodingSignal"/> after a registration or address change.
/// </summary>
internal sealed class GeocodingWorker(
    IServiceScopeFactory scopes,
    GeocodingSignal signal,
    IOptions<GeocodingOptions> options,
    ILogger<GeocodingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Geocoding is disabled (GEOCODING_ENABLED=false); clinics will not appear on the map");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            bool processed;
            try
            {
                processed = await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Geocoding round failed");
                processed = false;
            }

            try
            {
                if (processed) await Task.Delay(options.Value.RequestIntervalMilliseconds, stoppingToken);
                else await signal.WaitAsync(TimeSpan.FromSeconds(options.Value.IdlePollSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Geocodes the next clinic that needs it. Returns false when there was nothing to do.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var geocoder = scope.ServiceProvider.GetRequiredService<IGeocoder>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var retryBefore = now.AddHours(-options.Value.RetryAfterHours);

        var clinic = await db.Clinics
            .Where(c => c.Latitude == null && (c.GeocodedAt == null || c.GeocodedAt < retryBefore))
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (clinic is null) return false;

        GeoPoint? point = null;
        try
        {
            point = await geocoder.GeocodeAsync(clinic.Street, clinic.PostalCode, clinic.City, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Geocoding clinic {ClinicId} failed: {Reason}", clinic.Id, ex.GetType().Name);
        }

        clinic.GeocodedAt = now;
        if (point is not null)
        {
            clinic.Latitude = point.Latitude;
            clinic.Longitude = point.Longitude;
        }
        // Coordinates are not a business change: UpdatedAt stays as it is.
        await db.SaveChangesAsync(ct);
        return true;
    }
}
