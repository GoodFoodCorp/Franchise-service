using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Franchise.Application.Common;
using Franchise.Domain.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Franchise.Infrastructure.Geocoding;

/// <summary>Address autocomplete with Photon (komoot), the OpenStreetMap search
/// engine built for search-as-you-type — the public Nominatim forbids it.
/// The public instance (photon.komoot.io) suits moderate traffic; production
/// volumes use a self-hosted one through ADDRESS_SUGGEST_URL. Only street
/// addresses of the served countries are kept (plus towns and postal codes on
/// demand); answers are cached.</summary>
public sealed class PhotonAddressSuggester(
    HttpClient http,
    IMemoryCache cache,
    ServedCountries countries,
    ILogger<PhotonAddressSuggester> logger) : IAddressSuggester
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    // Countries writing the house number before the street ("12 rue X");
    // elsewhere in the EU it follows the street ("Hauptstraße 12").
    private static readonly HashSet<string> NumberFirst = ["fr", "lu", "ie", "mt", "cy"];

    // Photon's types for a town, whatever its size.
    private static readonly HashSet<string> Towns = ["city", "town", "village"];

    public async Task<IReadOnlyList<AddressSuggestion>> SuggestAsync(
        string query, GeoPoint? near, int limit, bool withPlaces = false, CancellationToken ct = default)
    {
        // Around a restaurant, only its surroundings can be delivered: the
        // search is bounded to ~30 km (Photon's ranking bias alone lets a
        // matching town name elsewhere win).
        var bias = near is { } p ? NearbyBounds(p) : string.Empty;
        var key = $"suggest:{query.ToLowerInvariant()}{bias}:{limit}:{withPlaces}";
        if (cache.TryGetValue(key, out IReadOnlyList<AddressSuggestion>? cached) && cached is not null)
        {
            return cached;
        }

        PhotonResponse? response;
        try
        {
            // Over-fetch: cities, shops and foreign results are filtered out.
            var url = $"api/?q={Uri.EscapeDataString(query)}&limit={Math.Min(limit * 4, 40)}&lang=fr{bias}";
            response = await http.GetFromJsonAsync<PhotonResponse>(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Address suggestions unavailable: {Error}", ex.Message);
            throw new GeocodingUnavailableException("Address suggestion provider unavailable.", ex);
        }

        var suggestions = Map(response, countries, limit, withPlaces);
        cache.Set(key, suggestions, Ttl);
        return suggestions;
    }

    private static string NearbyBounds(GeoPoint p)
    {
        static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
        const double dLat = 0.27, dLon = 0.4;
        return $"&lat={F(p.Latitude)}&lon={F(p.Longitude)}" +
               $"&bbox={F(p.Longitude - dLon)},{F(p.Latitude - dLat)},{F(p.Longitude + dLon)},{F(p.Latitude + dLat)}";
    }

    internal static IReadOnlyList<AddressSuggestion> Map(PhotonResponse? response, ServedCountries countries, int limit, bool withPlaces = false)
    {
        var result = new List<AddressSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in response?.Features ?? [])
        {
            var p = feature.Properties;
            if (p is null || feature.Geometry?.Coordinates is not [var lon, var lat, ..])
            {
                continue;
            }
            var code = p.CountryCode?.ToLowerInvariant();
            if (!countries.Contains(code))
            {
                continue;
            }
            if (withPlaces && Place(p, code!, lat, lon) is { } place)
            {
                if (seen.Add(place.Label))
                {
                    result.Add(place);
                }
                if (result.Count == limit)
                {
                    break;
                }
                continue;
            }
            if (p.Type != "house" && p.Type != "street")
            {
                continue;
            }

            var streetName = p.Type == "street" ? p.Name ?? p.Street : p.Street ?? p.Name;
            var city = p.City ?? p.District ?? p.County;
            if (string.IsNullOrWhiteSpace(streetName) || string.IsNullOrWhiteSpace(city))
            {
                continue;
            }

            var street = string.IsNullOrWhiteSpace(p.HouseNumber)
                ? streetName
                : NumberFirst.Contains(code!) ? $"{p.HouseNumber} {streetName}" : $"{streetName} {p.HouseNumber}";
            var locality = string.Join(' ', new[] { p.PostCode, city }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var label = $"{street}, {locality}, {p.Country}".TrimEnd(',', ' ');
            if (!seen.Add(label))
            {
                continue;
            }

            result.Add(new AddressSuggestion(label, street, p.PostCode ?? string.Empty, city, p.Country ?? string.Empty,
                code!.ToUpperInvariant(), lat, lon));
            if (result.Count == limit)
            {
                break;
            }
        }
        return result;
    }

    /// <summary>A town ("Rouen, France") or a postal code ("76000 Rouen,
    /// France"); null for anything else.</summary>
    private static AddressSuggestion? Place(PhotonProperties p, string code, double lat, double lon)
    {
        if (string.IsNullOrWhiteSpace(p.Name))
        {
            return null;
        }
        if (p.Type is not null && Towns.Contains(p.Type))
        {
            return new AddressSuggestion($"{p.Name}, {p.Country}".TrimEnd(',', ' '), string.Empty, p.PostCode ?? string.Empty,
                p.Name, p.Country ?? string.Empty, code.ToUpperInvariant(), lat, lon);
        }
        if (p.OsmValue == "postcode")
        {
            var locality = string.Join(' ', new[] { p.Name, p.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return new AddressSuggestion($"{locality}, {p.Country}".TrimEnd(',', ' '), string.Empty, p.Name,
                p.City ?? string.Empty, p.Country ?? string.Empty, code.ToUpperInvariant(), lat, lon);
        }
        return null;
    }

    internal sealed record PhotonResponse([property: JsonPropertyName("features")] List<PhotonFeature>? Features);

    internal sealed record PhotonFeature(
        [property: JsonPropertyName("geometry")] PhotonGeometry? Geometry,
        [property: JsonPropertyName("properties")] PhotonProperties? Properties);

    internal sealed record PhotonGeometry([property: JsonPropertyName("coordinates")] double[]? Coordinates);

    internal sealed record PhotonProperties(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("osm_value")] string? OsmValue,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("housenumber")] string? HouseNumber,
        [property: JsonPropertyName("street")] string? Street,
        [property: JsonPropertyName("postcode")] string? PostCode,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("district")] string? District,
        [property: JsonPropertyName("county")] string? County,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("countrycode")] string? CountryCode);
}

/// <summary>Used when suggestions are disabled (ADDRESS_SUGGEST_URL=disabled).</summary>
public sealed class DisabledAddressSuggester : IAddressSuggester
{
    public Task<IReadOnlyList<AddressSuggestion>> SuggestAsync(
        string query, GeoPoint? near, int limit, bool withPlaces = false, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AddressSuggestion>>([]);
}
