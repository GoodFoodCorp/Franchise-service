using Franchise.Domain.Errors;

namespace Franchise.Domain.Entities;

/// <summary>A supplier of one franchise (back-office franchisé: fournisseurs).</summary>
public sealed class Supplier
{
    private Supplier() { } // EF Core

    internal Supplier(Guid restaurantId, string name, string contactName, string email, string phone)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw DomainException.Validation("Supplier name is required.");
        }

        Id = Guid.NewGuid();
        RestaurantId = restaurantId;
        Name = name.Trim();
        ContactName = contactName?.Trim() ?? string.Empty;
        Email = email?.Trim() ?? string.Empty;
        Phone = phone?.Trim() ?? string.Empty;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    /// <summary>Owning franchise — the tenant isolation boundary.</summary>
    public Guid RestaurantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string ContactName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsOwnedBy(Guid restaurantId) => RestaurantId == restaurantId;
}
