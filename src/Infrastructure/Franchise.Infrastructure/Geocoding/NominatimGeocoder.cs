using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Franchise.Application.Common;
using Franchise.Domain.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Franchise.Infrastructure.Geocoding;

/// <summary>Geocodes with a Nominatim-compatible API (OpenStreetMap by default,
/// or a self-hosted instance via GEOCODER_URL for production volumes, since
/// the public one allows about one request per second). Results, including
/// misses, are cached: delivery addresses repeat a lot.</summary>
public sealed class NominatimGeocoder(HttpClient http, IMemoryCache cache, ILogger<NominatimGeocoder> logger) : IGeocoder
{
    private static readonly TimeSpan HitTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan MissTtl = TimeSpan.FromHours(1);

    private sealed record Place(
        [property: JsonPropertyName("lat")] string Lat,
        [property: JsonPropertyName("lon")] string Lon);

    public async Task<GeoPoint?> GeocodeAsync(string address, CancellationToken ct = default)
    {
        var key = "geo:" + address.Trim().ToLowerInvariant();
        if (cache.TryGetValue(key, out GeoPoint? cached))
        {
            return cached;
        }

        GeoPoint? result = null;
        try
        {
            var url = $"search?format=json&limit=1&countrycodes=fr&q={Uri.EscapeDataString(address)}";
            var places = await http.GetFromJsonAsync<List<Place>>(url, ct);
            if (places is [var first, ..] &&
                double.TryParse(first.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(first.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                result = new GeoPoint(lat, lon);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // An outage must not be cached as "address unknown".
            logger.LogWarning("Geocoding unavailable: {Error}", ex.Message);
            throw new GeocodingUnavailableException("Geocoding provider unavailable.", ex);
        }

        cache.Set(key, result, result is null ? MissTtl : HitTtl);
        return result;
    }
}

/// <summary>Used when geocoding is disabled (GEOCODER_URL=disabled): behaves
/// like an unavailable provider, so distance checks are skipped.</summary>
public sealed class DisabledGeocoder : IGeocoder
{
    public Task<GeoPoint?> GeocodeAsync(string address, CancellationToken ct = default) =>
        throw new GeocodingUnavailableException("Geocoding is disabled.");
}
