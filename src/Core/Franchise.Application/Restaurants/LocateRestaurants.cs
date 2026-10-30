using Franchise.Application.Common;
using Franchise.Domain.Repositories;
using MediatR;

namespace Franchise.Application.Restaurants;

/// <summary>Geocodes the restaurants that have no coordinates yet (created
/// before geocoding existed, or while the provider was down). Without them
/// the delivery radius is not enforced and couriers are not offered the
/// order nearby. <paramref name="Pause"/> spaces the provider calls (the
/// public Nominatim allows one request per second).</summary>
public sealed record LocateUnplacedRestaurantsCommand(TimeSpan Pause) : IRequest<int>;

public sealed class LocateUnplacedRestaurantsHandler(IRestaurantRepository restaurants, IUnitOfWork uow, IGeocoder geocoder)
    : IRequestHandler<LocateUnplacedRestaurantsCommand, int>
{
    public async Task<int> Handle(LocateUnplacedRestaurantsCommand cmd, CancellationToken ct)
    {
        var located = 0;
        var first = true;
        foreach (var restaurant in await restaurants.ListAsync(onlyActive: false, ct))
        {
            if (restaurant.Location is not null || string.IsNullOrWhiteSpace(restaurant.Address))
            {
                continue;
            }

            if (!first)
            {
                await Task.Delay(cmd.Pause, ct);
            }
            first = false;

            var location = await CreateRestaurantHandler.TryLocate(geocoder, restaurant, ct);
            if (location is null)
            {
                continue;
            }

            restaurant.SetLocation(location);
            await uow.SaveChangesAsync(ct);
            located++;
        }

        return located;
    }
}
