using Franchise.Application.Common;
using Franchise.Domain.ValueObjects;
using MediatR;

namespace Franchise.Application.Restaurants;

/// <summary>Address autocomplete for the checkout, the customer's saved
/// addresses and the restaurants' own address. <paramref name="Latitude"/> /
/// <paramref name="Longitude"/> rank the addresses near a restaurant first.
/// <paramref name="Places"/> adds towns and postal codes (the storefront's
/// "restaurants near…" search).</summary>
public sealed record SuggestAddressesQuery(string? Query, double? Latitude, double? Longitude, int Limit = 5, bool Places = false)
    : IRequest<IReadOnlyList<AddressSuggestion>>;

public sealed class SuggestAddressesHandler(IAddressSuggester suggester)
    : IRequestHandler<SuggestAddressesQuery, IReadOnlyList<AddressSuggestion>>
{
    public const int MinQueryLength = 3;
    public const int MaxQueryLength = 200;
    public const int MaxLimit = 8;

    public async Task<IReadOnlyList<AddressSuggestion>> Handle(SuggestAddressesQuery query, CancellationToken ct)
    {
        var text = (query.Query ?? string.Empty).Trim();
        if (text.Length < MinQueryLength || text.Length > MaxQueryLength)
        {
            return [];
        }

        GeoPoint? near = null;
        if (query.Latitude is { } lat && query.Longitude is { } lng && Math.Abs(lat) <= 90 && Math.Abs(lng) <= 180)
        {
            near = new GeoPoint(lat, lng);
        }

        try
        {
            return await suggester.SuggestAsync(text, near, Math.Clamp(query.Limit, 1, MaxLimit), query.Places, ct);
        }
        catch (GeocodingUnavailableException)
        {
            // Typing goes on without suggestions: the address is still
            // geocoded when the quote is asked for.
            return [];
        }
    }
}
