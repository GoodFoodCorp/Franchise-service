using Franchise.Domain.ValueObjects;

namespace Franchise.Application.Common;

/// <summary>A deliverable address proposed while the customer types it.</summary>
public sealed record AddressSuggestion(
    string Label,
    string Street,
    string PostalCode,
    string City,
    string Country,
    string CountryCode,
    double Latitude,
    double Longitude);

/// <summary>Search-as-you-type over street addresses, limited to the countries
/// the platform serves. <c>withPlaces</c> also proposes towns and postal codes
/// (finding the closest restaurants, not a delivery address: those have no
/// <see cref="AddressSuggestion.Street"/>). Throws
/// <see cref="GeocodingUnavailableException"/> when the provider cannot answer.</summary>
public interface IAddressSuggester
{
    Task<IReadOnlyList<AddressSuggestion>> SuggestAsync(
        string query, GeoPoint? near, int limit, bool withPlaces = false, CancellationToken ct = default);
}
