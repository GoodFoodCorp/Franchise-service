using System.Globalization;
using System.Text;
using Franchise.Domain.Errors;
using Franchise.Domain.ValueObjects;

namespace Franchise.Domain.Entities;

/// <summary>
/// A Good Food restaurant (franchise). This is the tenant every other service
/// scopes its data by: its Id is the tenant_id carried in JWTs and referenced
/// by menus, stock and orders.
/// </summary>
public sealed class Restaurant
{
    /// <summary>Time zone every opening hour is expressed in.</summary>
    public const string DefaultTimeZone = "Europe/Paris";

    /// <summary>Average urban courier speed used for delivery estimates.</summary>
    private const double CourierSpeedKmh = 15;

    /// <summary>Time for the courier to reach the counter and leave.</summary>
    private const int PickupMinutes = 5;

    private const int MaxOpeningSlots = 28;

    private readonly List<Supplier> _suppliers = [];

    private List<OpeningSlot> _openingHours = [];

    private Restaurant() { } // EF Core

    public Restaurant(string name, string slug, string address, string city, string plan = "free")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw DomainException.Validation("Restaurant name is required.");
        }

        Id = Guid.NewGuid();
        Name = name.Trim();
        Slug = string.IsNullOrWhiteSpace(slug) ? GenerateSlug(name) : slug.Trim().ToLowerInvariant();
        Address = address?.Trim() ?? string.Empty;
        City = city?.Trim() ?? string.Empty;
        Plan = plan;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Rehydrates a restaurant with a known id — used by the one-off
    /// import from auth-service so existing tenant ids (referenced by orders,
    /// stock and menus) are preserved.</summary>
    public static Restaurant Import(Guid id, string name, string slug, bool isActive, string plan)
    {
        var restaurant = new Restaurant(name, slug, string.Empty, string.Empty, plan)
        {
            Id = id,
            IsActive = isActive,
        };
        return restaurant;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string Plan { get; private set; } = "free";

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string Phone { get; private set; } = string.Empty;

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public string TimeZone { get; private set; } = DefaultTimeZone;

    /// <summary>Weekly opening periods. Empty means "no schedule set yet":
    /// the restaurant is then considered always open, as before schedules
    /// existed.</summary>
    public IReadOnlyList<OpeningSlot> OpeningHours => _openingHours.AsReadOnly();

    /// <summary>Lets a franchisee pause new orders (rush, incident) without
    /// touching the schedule.</summary>
    public bool AcceptingOrders { get; private set; } = true;

    public int PrepTimeMinutes { get; private set; } = 20;

    public double DeliveryRadiusKm { get; private set; } = 5;

    public long DeliveryFeeCents { get; private set; } = 299;

    public long MinimumOrderCents { get; private set; }

    /// <summary>Covers the dining room can seat per reservation slot.</summary>
    public int SeatingCapacity { get; private set; } = 40;

    public GeoPoint? Location => Latitude is { } lat && Longitude is { } lng ? new GeoPoint(lat, lng) : null;

    public IReadOnlyCollection<Supplier> Suppliers => _suppliers.AsReadOnly();

    public void Update(string name, string address, string city, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw DomainException.Validation("Restaurant name is required.");
        }

        Name = name.Trim();
        Address = address?.Trim() ?? string.Empty;
        City = city?.Trim() ?? string.Empty;
        IsActive = isActive;
    }

    public void SetLocation(GeoPoint? location)
    {
        Latitude = location?.Latitude;
        Longitude = location?.Longitude;
    }

    /// <summary>Updates how the restaurant operates: contact, schedule,
    /// delivery zone and pricing, kitchen pace and dining room size.</summary>
    public void UpdateOperations(
        string phone,
        string timeZone,
        IReadOnlyCollection<OpeningSlot> openingHours,
        int prepTimeMinutes,
        double deliveryRadiusKm,
        long deliveryFeeCents,
        long minimumOrderCents,
        int seatingCapacity)
    {
        if (string.IsNullOrWhiteSpace(timeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _))
        {
            throw DomainException.Validation("Unknown time zone.");
        }

        if (openingHours.Count > MaxOpeningSlots)
        {
            throw DomainException.Validation($"At most {MaxOpeningSlots} opening periods per week.");
        }

        foreach (var slot in openingHours)
        {
            slot.Validate();
        }

        if (prepTimeMinutes is < 1 or > 240)
        {
            throw DomainException.Validation("Preparation time must be between 1 and 240 minutes.");
        }

        if (deliveryRadiusKm is <= 0 or > 50)
        {
            throw DomainException.Validation("Delivery radius must be between 0 and 50 km.");
        }

        if (deliveryFeeCents < 0 || minimumOrderCents < 0)
        {
            throw DomainException.Validation("Fees and minimum order cannot be negative.");
        }

        if (seatingCapacity is < 0 or > 2000)
        {
            throw DomainException.Validation("Seating capacity must be between 0 and 2000.");
        }

        Phone = phone?.Trim() ?? string.Empty;
        TimeZone = timeZone;
        _openingHours = openingHours.OrderBy(s => s.Day).ThenBy(s => s.Opens).ToList();
        PrepTimeMinutes = prepTimeMinutes;
        DeliveryRadiusKm = deliveryRadiusKm;
        DeliveryFeeCents = deliveryFeeCents;
        MinimumOrderCents = minimumOrderCents;
        SeatingCapacity = seatingCapacity;
    }

    public void SetAcceptingOrders(bool accepting) => AcceptingOrders = accepting;

    /// <summary>Whether the restaurant is open at the given instant, in its
    /// own time zone.</summary>
    public bool IsOpenAt(DateTimeOffset instant)
    {
        if (_openingHours.Count == 0)
        {
            return true;
        }

        var local = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(TimeZone));
        var time = TimeOnly.FromDateTime(local.DateTime);
        return _openingHours.Any(s => s.Contains(local.DayOfWeek, time));
    }

    /// <summary>Decides whether an order can be taken now for the drop-off
    /// point, and at what delivery fee and estimated time. A null drop-off
    /// with <paramref name="dropoffLookedUp"/> means the address does not
    /// exist; without it (geocoding unavailable) the distance is simply not
    /// checked, so an outage of the provider never stops the restaurant.</summary>
    public DeliveryQuote Quote(DateTimeOffset now, GeoPoint? dropoff, bool dropoffLookedUp = true)
    {
        var isOpen = IsOpenAt(now);
        var pickup = Location;
        double? distance = pickup is { } p && dropoff is { } d ? Math.Round(p.DistanceKmTo(d), 2) : null;
        var travelMinutes = distance is { } km ? (int)Math.Ceiling(km / CourierSpeedKmh * 60) : 0;
        var eta = PrepTimeMinutes + PickupMinutes + travelMinutes;

        var refusal = QuoteRefusal.None;
        if (!IsActive)
        {
            refusal = QuoteRefusal.Inactive;
        }
        else if (!isOpen)
        {
            refusal = QuoteRefusal.Closed;
        }
        else if (!AcceptingOrders)
        {
            refusal = QuoteRefusal.Paused;
        }
        else if (pickup is not null && dropoff is null && dropoffLookedUp)
        {
            refusal = QuoteRefusal.UnknownAddress;
        }
        else if (distance > DeliveryRadiusKm)
        {
            refusal = QuoteRefusal.OutOfZone;
        }

        return new DeliveryQuote(refusal == QuoteRefusal.None, refusal, isOpen, AcceptingOrders, distance,
            DeliveryFeeCents, MinimumOrderCents, PrepTimeMinutes, eta, pickup, dropoff);
    }

    /// <summary>Adds a supplier to this franchise (tenant boundary enforced here).</summary>
    public Supplier AddSupplier(string name, string contactName, string email, string phone)
    {
        var supplier = new Supplier(Id, name, contactName, email, phone);
        _suppliers.Add(supplier);
        return supplier;
    }

    /// <summary>URL-friendly slug: lowercased, accents stripped, spaces to dashes.</summary>
    public static string GenerateSlug(string value)
    {
        // Decompose accented characters so diacritics can be dropped (é → e).
        var normalized = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is >= 'a' and <= 'z' || c is >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (c is ' ' or '-' or '_')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }
}
