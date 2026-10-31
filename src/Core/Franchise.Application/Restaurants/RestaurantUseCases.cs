using FluentValidation;
using Franchise.Application.Common;
using Franchise.Domain.Errors;
using Franchise.Domain.Repositories;
using MediatR;
using RestaurantEntity = Franchise.Domain.Entities.Restaurant;

namespace Franchise.Application.Restaurants;

// ── Queries (public: the storefront lists restaurants) ──────

/// <summary>Lists restaurants. Inactive ones are only listed for head office.</summary>
public sealed record GetRestaurantsQuery(bool IncludeInactive = false, CurrentUser? User = null)
    : IRequest<IReadOnlyList<RestaurantDto>>;

public sealed class GetRestaurantsHandler(IRestaurantRepository restaurants, IClock clock)
    : IRequestHandler<GetRestaurantsQuery, IReadOnlyList<RestaurantDto>>
{
    public async Task<IReadOnlyList<RestaurantDto>> Handle(GetRestaurantsQuery query, CancellationToken ct)
    {
        var onlyActive = !(query.IncludeInactive && query.User?.IsAdmin == true);
        var list = await restaurants.ListAsync(onlyActive, ct);
        var now = clock.UtcNow;
        return list.Select(r => r.ToDto(now)).ToList();
    }
}

public sealed record GetRestaurantByIdQuery(Guid Id) : IRequest<RestaurantDto>;

public sealed class GetRestaurantByIdHandler(IRestaurantRepository restaurants, IClock clock)
    : IRequestHandler<GetRestaurantByIdQuery, RestaurantDto>
{
    public async Task<RestaurantDto> Handle(GetRestaurantByIdQuery query, CancellationToken ct)
    {
        var restaurant = await restaurants.GetByIdAsync(query.Id, ct)
            ?? throw DomainException.NotFound("Restaurant not found.");
        return restaurant.ToDto(clock.UtcNow);
    }
}

/// <summary>Can this restaurant take an order for that address right now, and
/// at what delivery fee? Used by the storefront before checkout and by
/// order-service when the order is placed. Coordinates, when known, spare the
/// geocoding.</summary>
public sealed record QuoteDeliveryQuery(Guid RestaurantId, string? Address, double? Latitude, double? Longitude)
    : IRequest<DeliveryQuoteDto>;

public sealed class QuoteDeliveryHandler(IRestaurantRepository restaurants, IGeocoder geocoder, IClock clock)
    : IRequestHandler<QuoteDeliveryQuery, DeliveryQuoteDto>
{
    public async Task<DeliveryQuoteDto> Handle(QuoteDeliveryQuery query, CancellationToken ct)
    {
        var restaurant = await restaurants.GetByIdAsync(query.RestaurantId, ct)
            ?? throw DomainException.NotFound("Restaurant not found.");

        if (query is { Latitude: { } lat, Longitude: { } lng })
        {
            return restaurant.Quote(clock.UtcNow, new Domain.ValueObjects.GeoPoint(lat, lng)).ToDto(restaurant.Id);
        }

        if (string.IsNullOrWhiteSpace(query.Address))
        {
            return restaurant.Quote(clock.UtcNow, null, dropoffLookedUp: false).ToDto(restaurant.Id);
        }

        try
        {
            var dropoff = await geocoder.GeocodeAsync(query.Address, ct);
            return restaurant.Quote(clock.UtcNow, dropoff).ToDto(restaurant.Id);
        }
        catch (GeocodingUnavailableException)
        {
            return restaurant.Quote(clock.UtcNow, null, dropoffLookedUp: false).ToDto(restaurant.Id);
        }
    }
}

// ── Commands (head office manages the franchise network) ────

public sealed record CreateRestaurantCommand(
    CurrentUser User,
    string Name,
    string Slug,
    string Address,
    string City,
    string Plan) : IRequest<RestaurantDto>;

public sealed class CreateRestaurantValidator : AbstractValidator<CreateRestaurantCommand>
{
    public CreateRestaurantValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Address).NotEmpty().MaximumLength(300);
        RuleFor(c => c.City).NotEmpty().MaximumLength(120);
    }
}

public sealed class CreateRestaurantHandler(IRestaurantRepository restaurants, IUnitOfWork uow, IGeocoder geocoder, IClock clock)
    : IRequestHandler<CreateRestaurantCommand, RestaurantDto>
{
    public async Task<RestaurantDto> Handle(CreateRestaurantCommand cmd, CancellationToken ct)
    {
        if (!cmd.User.IsAdmin)
        {
            throw DomainException.Forbidden("Only head office can create a restaurant.");
        }

        var slug = string.IsNullOrWhiteSpace(cmd.Slug) ? RestaurantEntity.GenerateSlug(cmd.Name) : cmd.Slug;
        if (await restaurants.GetBySlugAsync(slug, ct) is not null)
        {
            throw DomainException.Conflict($"A restaurant with slug '{slug}' already exists.");
        }

        var restaurant = new RestaurantEntity(cmd.Name, slug, cmd.Address, cmd.City, cmd.Plan ?? "free");
        restaurant.SetLocation(await LocateOrRefuse(geocoder, restaurant, ct));
        await restaurants.AddAsync(restaurant, ct);
        await uow.SaveChangesAsync(ct);
        return restaurant.ToDto(clock.UtcNow);
    }

    /// <summary>Locates a new or moved restaurant. An address the provider
    /// does not know is refused: nobody could be delivered from it. While the
    /// provider is down the restaurant stays unlocated (located again on the
    /// next update or listing).</summary>
    internal static async Task<Domain.ValueObjects.GeoPoint?> LocateOrRefuse(IGeocoder geocoder, RestaurantEntity r, CancellationToken ct)
    {
        var address = $"{r.Address}, {r.City}".Trim(' ', ',');
        try
        {
            return await geocoder.GeocodeAsync(address, ct)
                ?? throw DomainException.Validation("Address not found: check the number, street and city.");
        }
        catch (GeocodingUnavailableException)
        {
            return r.Location;
        }
    }

    /// <summary>Locates the restaurant; unknown while the provider is down
    /// (located again on the next address update).</summary>
    internal static async Task<Domain.ValueObjects.GeoPoint?> TryLocate(IGeocoder geocoder, RestaurantEntity r, CancellationToken ct)
    {
        var address = $"{r.Address}, {r.City}".Trim(' ', ',');
        if (address.Length == 0)
        {
            return null;
        }

        try
        {
            return await geocoder.GeocodeAsync(address, ct);
        }
        catch (GeocodingUnavailableException)
        {
            return r.Location;
        }
    }
}

public sealed record UpdateRestaurantCommand(
    CurrentUser User,
    Guid Id,
    string Name,
    string Address,
    string City,
    bool IsActive) : IRequest<RestaurantDto>;

public sealed class UpdateRestaurantHandler(IRestaurantRepository restaurants, IUnitOfWork uow, IGeocoder geocoder, IClock clock)
    : IRequestHandler<UpdateRestaurantCommand, RestaurantDto>
{
    public async Task<RestaurantDto> Handle(UpdateRestaurantCommand cmd, CancellationToken ct)
    {
        var restaurant = await RestaurantAccess.LoadEditable(restaurants, cmd.User, cmd.Id, ct);

        var moved = restaurant.Address != (cmd.Address ?? string.Empty).Trim() ||
                    restaurant.City != (cmd.City ?? string.Empty).Trim();
        var unlocated = restaurant.Location is null;
        // Only head office opens or closes a franchise in the network.
        var isActive = cmd.User.IsAdmin ? cmd.IsActive : restaurant.IsActive;
        restaurant.Update(cmd.Name, cmd.Address ?? string.Empty, cmd.City ?? string.Empty, isActive);
        if (moved)
        {
            // A new address must exist.
            restaurant.SetLocation(await CreateRestaurantHandler.LocateOrRefuse(geocoder, restaurant, ct));
        }
        else if (unlocated)
        {
            // Same address, still unknown: try again, but never block the
            // update (head office must be able to close such a restaurant).
            restaurant.SetLocation(await CreateRestaurantHandler.TryLocate(geocoder, restaurant, ct));
        }

        await uow.SaveChangesAsync(ct);
        return restaurant.ToDto(clock.UtcNow);
    }
}

public sealed record UpdateOperationsCommand(
    CurrentUser User,
    Guid Id,
    string Phone,
    string TimeZone,
    IReadOnlyList<OpeningSlotDto> OpeningHours,
    int PrepTimeMinutes,
    double DeliveryRadiusKm,
    long DeliveryFeeCents,
    long MinimumOrderCents,
    int SeatingCapacity) : IRequest<RestaurantDto>;

public sealed class UpdateOperationsValidator : AbstractValidator<UpdateOperationsCommand>
{
    public UpdateOperationsValidator()
    {
        RuleFor(c => c.Phone).MaximumLength(50);
        RuleFor(c => c.OpeningHours).NotNull();
    }
}

public sealed class UpdateOperationsHandler(IRestaurantRepository restaurants, IUnitOfWork uow, IClock clock)
    : IRequestHandler<UpdateOperationsCommand, RestaurantDto>
{
    public async Task<RestaurantDto> Handle(UpdateOperationsCommand cmd, CancellationToken ct)
    {
        var restaurant = await RestaurantAccess.LoadEditable(restaurants, cmd.User, cmd.Id, ct);
        restaurant.UpdateOperations(
            cmd.Phone,
            string.IsNullOrWhiteSpace(cmd.TimeZone) ? RestaurantEntity.DefaultTimeZone : cmd.TimeZone,
            cmd.OpeningHours.Select(s => s.ToDomain()).ToList(),
            cmd.PrepTimeMinutes,
            cmd.DeliveryRadiusKm,
            cmd.DeliveryFeeCents,
            cmd.MinimumOrderCents,
            cmd.SeatingCapacity);
        await uow.SaveChangesAsync(ct);
        return restaurant.ToDto(clock.UtcNow);
    }
}

/// <summary>Pauses or resumes new orders (kitchen rush, incident).</summary>
public sealed record SetOrderIntakeCommand(CurrentUser User, Guid Id, bool AcceptingOrders) : IRequest<RestaurantDto>;

public sealed class SetOrderIntakeHandler(IRestaurantRepository restaurants, IUnitOfWork uow, IClock clock)
    : IRequestHandler<SetOrderIntakeCommand, RestaurantDto>
{
    public async Task<RestaurantDto> Handle(SetOrderIntakeCommand cmd, CancellationToken ct)
    {
        var restaurant = await RestaurantAccess.LoadEditable(restaurants, cmd.User, cmd.Id, ct);
        restaurant.SetAcceptingOrders(cmd.AcceptingOrders);
        await uow.SaveChangesAsync(ct);
        return restaurant.ToDto(clock.UtcNow);
    }
}

internal static class RestaurantAccess
{
    /// <summary>Head office may edit any restaurant; a manager only their own.</summary>
    public static async Task<RestaurantEntity> LoadEditable(IRestaurantRepository restaurants, CurrentUser user, Guid id, CancellationToken ct)
    {
        var restaurant = await restaurants.GetByIdAsync(id, ct)
            ?? throw DomainException.NotFound("Restaurant not found.");
        if (!user.IsAdmin && user.RequireOwnRestaurant() != restaurant.Id)
        {
            throw DomainException.Forbidden("You can only edit your own restaurant.");
        }

        return restaurant;
    }
}
