using FluentAssertions;
using Franchise.Domain.Entities;
using Franchise.Domain.Errors;

namespace Franchise.Domain.Tests;

public class RestaurantTests
{
    [Theory]
    [InlineData("Good Food République", "good-food-republique")]
    [InlineData("Good Food  Montparnasse", "good-food-montparnasse")]
    [InlineData("Crêperie de l'Île", "creperie-de-lile")]
    public void Slug_is_url_friendly_and_accent_free(string name, string expected)
    {
        var restaurant = new Restaurant(name, string.Empty, "12 place", "Paris");
        restaurant.Slug.Should().Be(expected);
        restaurant.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Blank_name_is_rejected()
    {
        var act = () => new Restaurant("  ", "x", string.Empty, string.Empty);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.Validation);
    }

    [Fact]
    public void Import_preserves_the_original_id()
    {
        var id = Guid.NewGuid();
        var restaurant = Restaurant.Import(id, "Legacy Tenant", "legacy", true, "free");

        restaurant.Id.Should().Be(id, "orders, stock and menus reference this id");
        restaurant.Slug.Should().Be("legacy");
    }

    [Fact]
    public void Suppliers_are_attached_to_their_restaurant()
    {
        var restaurant = new Restaurant("Resto", "resto", string.Empty, string.Empty);
        var supplier = restaurant.AddSupplier("Metro", "Jean", "jean@metro.fr", "0102030405");

        supplier.RestaurantId.Should().Be(restaurant.Id);
        supplier.IsOwnedBy(restaurant.Id).Should().BeTrue();
        supplier.IsOwnedBy(Guid.NewGuid()).Should().BeFalse();
        restaurant.Suppliers.Should().ContainSingle();
    }

    [Fact]
    public void Supplier_requires_a_name()
    {
        var restaurant = new Restaurant("Resto", "resto", string.Empty, string.Empty);
        var act = () => restaurant.AddSupplier(" ", "x", "a@b.fr", "01");
        act.Should().Throw<DomainException>();
    }
}
