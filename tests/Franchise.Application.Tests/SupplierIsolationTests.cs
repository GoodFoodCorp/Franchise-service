using FluentAssertions;
using Franchise.Application.Common;
using Franchise.Application.Suppliers;
using Franchise.Domain.Entities;
using Franchise.Domain.Errors;
using Franchise.Domain.Repositories;
using Moq;

namespace Franchise.Application.Tests;

public class SupplierIsolationTests
{
    private static readonly Guid RestaurantA = Guid.NewGuid();
    private static readonly Guid RestaurantB = Guid.NewGuid();

    private static CurrentUser Manager(Guid restaurantId) => new("mgr", restaurantId, ["manager"]);

    private readonly Mock<ISupplierRepository> _suppliers = new();
    private readonly Mock<IRestaurantRepository> _restaurants = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    [Fact]
    public async Task Manager_creates_a_supplier_in_their_own_restaurant()
    {
        var restaurant = Restaurant.Import(RestaurantA, "A", "a", true, "free");
        _restaurants.Setup(r => r.GetByIdAsync(RestaurantA, It.IsAny<CancellationToken>())).ReturnsAsync(restaurant);

        var handler = new CreateSupplierHandler(_restaurants.Object, _uow.Object);
        var dto = await handler.Handle(
            new CreateSupplierCommand(Manager(RestaurantA), "Metro", "Jean", "j@metro.fr", "01"), default);

        dto.RestaurantId.Should().Be(RestaurantA);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Manager_cannot_delete_another_restaurants_supplier()
    {
        var foreign = Restaurant.Import(RestaurantB, "B", "b", true, "free").AddSupplier("Sysco", "x", "x@x.fr", "01");
        _suppliers.Setup(s => s.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var handler = new DeleteSupplierHandler(_suppliers.Object, _uow.Object);
        var act = () => handler.Handle(new DeleteSupplierCommand(Manager(RestaurantA), foreign.Id), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Forbidden);
        _suppliers.Verify(s => s.RemoveAsync(It.IsAny<Supplier>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_customer_cannot_manage_suppliers()
    {
        var customer = new CurrentUser("cust", null, ["user"]);
        var handler = new GetSuppliersHandler(_suppliers.Object);

        var act = () => handler.Handle(new GetSuppliersQuery(customer), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Forbidden);
    }

    [Fact]
    public async Task A_manager_without_restaurant_is_refused()
    {
        var orphan = new CurrentUser("mgr", null, ["manager"]);
        var handler = new GetSuppliersHandler(_suppliers.Object);

        var act = () => handler.Handle(new GetSuppliersQuery(orphan), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Forbidden);
    }
}
