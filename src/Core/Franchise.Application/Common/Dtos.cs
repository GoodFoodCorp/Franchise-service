using Franchise.Domain.Entities;

namespace Franchise.Application.Common;

public sealed record RestaurantDto(
    Guid Id,
    string Name,
    string Slug,
    string Address,
    string City,
    string Plan,
    bool IsActive);

public sealed record SupplierDto(
    Guid Id,
    Guid RestaurantId,
    string Name,
    string ContactName,
    string Email,
    string Phone,
    DateTimeOffset CreatedAt);

public static class DtoMapping
{
    public static RestaurantDto ToDto(this Restaurant r) =>
        new(r.Id, r.Name, r.Slug, r.Address, r.City, r.Plan, r.IsActive);

    public static SupplierDto ToDto(this Supplier s) =>
        new(s.Id, s.RestaurantId, s.Name, s.ContactName, s.Email, s.Phone, s.CreatedAt);
}
