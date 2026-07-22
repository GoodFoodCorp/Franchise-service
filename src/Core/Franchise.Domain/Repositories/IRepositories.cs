using Franchise.Domain.Entities;

namespace Franchise.Domain.Repositories;

public interface IRestaurantRepository
{
    Task<Restaurant?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Restaurant?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<IReadOnlyList<Restaurant>> ListAsync(bool onlyActive, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    Task AddAsync(Restaurant restaurant, CancellationToken ct = default);
}

public interface ISupplierRepository
{
    Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Supplier>> ListByRestaurantAsync(Guid restaurantId, CancellationToken ct = default);

    Task RemoveAsync(Supplier supplier, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}
