using Franchise.Domain.Entities;
using Franchise.Domain.ValueObjects;

namespace Franchise.Application.Common;

public sealed record OpeningSlotDto(DayOfWeek Day, string Opens, string Closes);

public sealed record RestaurantDto(
    Guid Id,
    string Name,
    string Slug,
    string Address,
    string City,
    string Plan,
    bool IsActive,
    string Phone,
    double? Latitude,
    double? Longitude,
    string TimeZone,
    IReadOnlyList<OpeningSlotDto> OpeningHours,
    bool IsOpenNow,
    bool AcceptingOrders,
    int PrepTimeMinutes,
    double DeliveryRadiusKm,
    long DeliveryFeeCents,
    long MinimumOrderCents,
    int SeatingCapacity);

public sealed record SupplierDto(
    Guid Id,
    Guid RestaurantId,
    string Name,
    string ContactName,
    string Email,
    string Phone,
    DateTimeOffset CreatedAt);

public sealed record DeliveryQuoteDto(
    Guid RestaurantId,
    bool CanOrder,
    string? Refusal,
    bool IsOpen,
    bool AcceptingOrders,
    double? DistanceKm,
    long DeliveryFeeCents,
    long MinimumOrderCents,
    int PrepTimeMinutes,
    int EstimatedDeliveryMinutes,
    double? PickupLatitude,
    double? PickupLongitude,
    double? DropoffLatitude,
    double? DropoffLongitude);

public static class DtoMapping
{
    private const string TimeFormat = "HH:mm";

    public static RestaurantDto ToDto(this Restaurant r, DateTimeOffset now) =>
        new(r.Id, r.Name, r.Slug, r.Address, r.City, r.Plan, r.IsActive,
            r.Phone, r.Latitude, r.Longitude, r.TimeZone,
            r.OpeningHours.Select(s => new OpeningSlotDto(s.Day, s.Opens.ToString(TimeFormat), s.Closes.ToString(TimeFormat))).ToList(),
            r.IsOpenAt(now), r.AcceptingOrders, r.PrepTimeMinutes, r.DeliveryRadiusKm,
            r.DeliveryFeeCents, r.MinimumOrderCents, r.SeatingCapacity);

    public static SupplierDto ToDto(this Supplier s) =>
        new(s.Id, s.RestaurantId, s.Name, s.ContactName, s.Email, s.Phone, s.CreatedAt);

    public static DeliveryQuoteDto ToDto(this DeliveryQuote q, Guid restaurantId) =>
        new(restaurantId, q.CanOrder, q.Refusal == QuoteRefusal.None ? null : q.Refusal.ToString().ToUpperInvariant(),
            q.IsOpen, q.AcceptingOrders, q.DistanceKm, q.DeliveryFeeCents, q.MinimumOrderCents,
            q.PrepTimeMinutes, q.EstimatedDeliveryMinutes,
            q.Pickup?.Latitude, q.Pickup?.Longitude, q.Dropoff?.Latitude, q.Dropoff?.Longitude);

    /// <summary>Parses "HH:mm" slots coming from the API.</summary>
    public static OpeningSlot ToDomain(this OpeningSlotDto s)
    {
        if (!TimeOnly.TryParseExact(s.Opens, TimeFormat, out var opens) ||
            !TimeOnly.TryParseExact(s.Closes, TimeFormat, out var closes))
        {
            throw Franchise.Domain.Errors.DomainException.Validation("Opening hours must use the HH:mm format.");
        }

        return new OpeningSlot(s.Day, opens, closes);
    }
}
