using FluentAssertions;
using Franchise.Application.Common;
using Franchise.Application.Restaurants;
using Franchise.Domain.Entities;
using Franchise.Domain.Errors;
using Franchise.Domain.Repositories;
using Franchise.Domain.ValueObjects;
using Moq;

namespace Franchise.Application.Tests;

/// <summary>A restaurant nobody can locate cannot deliver: its address must exist.</summary>
public class RestaurantAddressTests
{
    private static readonly GeoPoint Bastille = new(48.8532, 2.3692);
    private static readonly CurrentUser HeadOffice = new("admin", null, ["admin"]);

    private readonly Mock<IRestaurantRepository> _restaurants = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IGeocoder> _geocoder = new();
    private readonly Mock<IClock> _clock = new();

    public RestaurantAddressTests() => _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

    private CreateRestaurantHandler Create() => new(_restaurants.Object, _uow.Object, _geocoder.Object, _clock.Object);

    private static CreateRestaurantCommand Command(string address) => new(HeadOffice, "Bastille", "bastille", address, "Paris", "free");

    [Fact]
    public async Task A_known_address_is_located()
    {
        _geocoder.Setup(g => g.GeocodeAsync("8 rue de la Roquette, Paris", It.IsAny<CancellationToken>())).ReturnsAsync(Bastille);

        var dto = await Create().Handle(Command("8 rue de la Roquette"), default);

        dto.Latitude.Should().Be(Bastille.Latitude);
        _restaurants.Verify(r => r.AddAsync(It.IsAny<Restaurant>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_unknown_address_is_refused()
    {
        _geocoder.Setup(g => g.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GeoPoint?)null);

        var act = () => Create().Handle(Command("zzzz 99999"), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Validation);
        _restaurants.Verify(r => r.AddAsync(It.IsAny<Restaurant>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_restaurant_is_created_unlocated_while_geocoding_is_down()
    {
        _geocoder.Setup(g => g.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GeocodingUnavailableException("down"));

        var dto = await Create().Handle(Command("8 rue de la Roquette"), default);

        dto.Latitude.Should().BeNull();
    }

    [Fact]
    public async Task Moving_to_an_unknown_address_is_refused()
    {
        var id = Guid.NewGuid();
        var restaurant = Restaurant.Import(id, "A", "a", true, "free");
        restaurant.SetLocation(Bastille);
        _restaurants.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(restaurant);
        _geocoder.Setup(g => g.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GeoPoint?)null);
        var update = new UpdateRestaurantHandler(_restaurants.Object, _uow.Object, _geocoder.Object, _clock.Object);

        var act = () => update.Handle(new UpdateRestaurantCommand(HeadOffice, id, "A", "nulle part", "Atlantis", true), default);

        await act.Should().ThrowAsync<DomainException>();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
