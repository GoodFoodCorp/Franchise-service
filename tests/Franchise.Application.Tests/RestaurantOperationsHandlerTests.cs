using FluentAssertions;
using Franchise.Application.Common;
using Franchise.Application.Restaurants;
using Franchise.Domain.Entities;
using Franchise.Domain.Errors;
using Franchise.Domain.Repositories;
using Franchise.Domain.ValueObjects;
using Moq;

namespace Franchise.Application.Tests;

public class RestaurantOperationsHandlerTests
{
    private static readonly Guid RestaurantA = Guid.NewGuid();
    private static readonly GeoPoint Republique = new(48.8674, 2.3636);
    private static readonly GeoPoint Bastille = new(48.8532, 2.3692);

    private readonly Mock<IRestaurantRepository> _restaurants = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IGeocoder> _geocoder = new();
    private readonly Mock<IClock> _clock = new();

    public RestaurantOperationsHandlerTests()
    {
        var restaurant = Restaurant.Import(RestaurantA, "A", "a", true, "free");
        restaurant.SetLocation(Republique);
        _restaurants.Setup(r => r.GetByIdAsync(RestaurantA, It.IsAny<CancellationToken>())).ReturnsAsync(restaurant);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
    }

    private static CurrentUser Manager(Guid restaurantId) => new("mgr", restaurantId, ["manager"]);

    private QuoteDeliveryHandler QuoteHandler() => new(_restaurants.Object, _geocoder.Object, _clock.Object);

    [Fact]
    public async Task Quote_geocodes_the_address()
    {
        _geocoder.Setup(g => g.GeocodeAsync("1 rue de la Roquette, Paris", It.IsAny<CancellationToken>())).ReturnsAsync(Bastille);

        var quote = await QuoteHandler().Handle(new QuoteDeliveryQuery(RestaurantA, "1 rue de la Roquette, Paris", null, null), default);

        quote.CanOrder.Should().BeTrue();
        quote.DistanceKm.Should().BeGreaterThan(0);
        quote.DropoffLatitude.Should().Be(Bastille.Latitude);
        quote.PickupLatitude.Should().Be(Republique.Latitude);
    }

    [Fact]
    public async Task Quote_uses_given_coordinates_without_geocoding()
    {
        var quote = await QuoteHandler().Handle(new QuoteDeliveryQuery(RestaurantA, "ignored", Bastille.Latitude, Bastille.Longitude), default);

        quote.CanOrder.Should().BeTrue();
        _geocoder.Verify(g => g.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Quote_refuses_an_address_that_does_not_exist()
    {
        _geocoder.Setup(g => g.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GeoPoint?)null);

        var quote = await QuoteHandler().Handle(new QuoteDeliveryQuery(RestaurantA, "nowhere", null, null), default);

        quote.CanOrder.Should().BeFalse();
        quote.Refusal.Should().Be("UNKNOWNADDRESS");
    }

    [Fact]
    public async Task Quote_fails_open_when_geocoding_is_down()
    {
        _geocoder.Setup(g => g.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GeocodingUnavailableException("down"));

        var quote = await QuoteHandler().Handle(new QuoteDeliveryQuery(RestaurantA, "12 rue X", null, null), default);

        quote.CanOrder.Should().BeTrue();
        quote.DistanceKm.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_restaurant_is_not_found()
    {
        var act = () => QuoteHandler().Handle(new QuoteDeliveryQuery(Guid.NewGuid(), null, null, null), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.NotFound);
    }

    [Fact]
    public async Task Manager_updates_operations_of_their_own_restaurant_only()
    {
        var handler = new UpdateOperationsHandler(_restaurants.Object, _uow.Object, _clock.Object);
        var hours = new List<OpeningSlotDto> { new(DayOfWeek.Monday, "11:30", "14:30") };

        var dto = await handler.Handle(new UpdateOperationsCommand(Manager(RestaurantA), RestaurantA, "0102", "Europe/Paris",
            hours, 25, 4, 350, 1200, 60), default);

        dto.DeliveryFeeCents.Should().Be(350);
        dto.OpeningHours.Should().ContainSingle().Which.Opens.Should().Be("11:30");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var other = Guid.NewGuid();
        var act = () => handler.Handle(new UpdateOperationsCommand(Manager(other), RestaurantA, "", "Europe/Paris", hours, 25, 4, 0, 0, 0), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Forbidden);
    }

    [Fact]
    public async Task Malformed_opening_hours_are_rejected()
    {
        var handler = new UpdateOperationsHandler(_restaurants.Object, _uow.Object, _clock.Object);
        var act = () => handler.Handle(new UpdateOperationsCommand(Manager(RestaurantA), RestaurantA, "", "Europe/Paris",
            [new OpeningSlotDto(DayOfWeek.Monday, "midi", "14h")], 25, 4, 0, 0, 0), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Validation);
    }

    [Fact]
    public async Task Manager_can_pause_orders_but_not_close_the_franchise()
    {
        var pause = new SetOrderIntakeHandler(_restaurants.Object, _uow.Object, _clock.Object);
        (await pause.Handle(new SetOrderIntakeCommand(Manager(RestaurantA), RestaurantA, false), default))
            .AcceptingOrders.Should().BeFalse();

        _geocoder.Setup(g => g.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Republique);
        var update = new UpdateRestaurantHandler(_restaurants.Object, _uow.Object, _geocoder.Object, _clock.Object);
        var dto = await update.Handle(new UpdateRestaurantCommand(Manager(RestaurantA), RestaurantA, "A", "1 rue", "Paris", IsActive: false), default);

        dto.IsActive.Should().BeTrue("only head office opens or closes a franchise");
        dto.Latitude.Should().Be(Republique.Latitude, "a moved restaurant is located again");
    }

    [Fact]
    public async Task Inactive_restaurants_are_listed_for_head_office_only()
    {
        _restaurants.Setup(r => r.ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var handler = new GetRestaurantsHandler(_restaurants.Object, _clock.Object);

        await handler.Handle(new GetRestaurantsQuery(IncludeInactive: true, User: null), default);
        await handler.Handle(new GetRestaurantsQuery(IncludeInactive: true, User: new CurrentUser("a", null, ["admin"])), default);

        _restaurants.Verify(r => r.ListAsync(true, It.IsAny<CancellationToken>()), Times.Once, "anonymous: active only");
        _restaurants.Verify(r => r.ListAsync(false, It.IsAny<CancellationToken>()), Times.Once, "admin: everything");
    }
}
