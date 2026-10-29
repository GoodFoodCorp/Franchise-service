namespace Franchise.Domain.ValueObjects;

/// <summary>Why an order cannot be taken right now.</summary>
public enum QuoteRefusal
{
    None,
    Inactive,
    Closed,
    Paused,
    OutOfZone,
    UnknownAddress,
}

/// <summary>What a restaurant answers to "can you deliver this address now,
/// and for how much".</summary>
public sealed record DeliveryQuote(
    bool CanOrder,
    QuoteRefusal Refusal,
    bool IsOpen,
    bool AcceptingOrders,
    double? DistanceKm,
    long DeliveryFeeCents,
    long MinimumOrderCents,
    int PrepTimeMinutes,
    int EstimatedDeliveryMinutes,
    GeoPoint? Pickup,
    GeoPoint? Dropoff);
