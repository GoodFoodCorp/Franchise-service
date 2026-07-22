using Franchise.Domain.Entities;
using Franchise.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Franchise.Infrastructure.Persistence;

public sealed class RestaurantRepository(FranchiseDbContext db) : IRestaurantRepository
{
    public Task<Restaurant?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Restaurants.Include(r => r.Suppliers).FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Restaurant?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Restaurants.FirstOrDefaultAsync(r => r.Slug == slug, ct);

    public async Task<IReadOnlyList<Restaurant>> ListAsync(bool onlyActive, CancellationToken ct = default) =>
        await db.Restaurants
            .Where(r => !onlyActive || r.IsActive)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

    public Task<int> CountAsync(CancellationToken ct = default) => db.Restaurants.CountAsync(ct);

    public async Task AddAsync(Restaurant restaurant, CancellationToken ct = default) =>
        await db.Restaurants.AddAsync(restaurant, ct);
}

public sealed class SupplierRepository(FranchiseDbContext db) : ISupplierRepository
{
    public Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Supplier>> ListByRestaurantAsync(Guid restaurantId, CancellationToken ct = default) =>
        await db.Suppliers
            .Where(s => s.RestaurantId == restaurantId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public Task RemoveAsync(Supplier supplier, CancellationToken ct = default)
    {
        db.Suppliers.Remove(supplier);
        return Task.CompletedTask;
    }
}

public sealed class UnitOfWork(FranchiseDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
