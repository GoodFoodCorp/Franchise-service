using System.Globalization;
using System.Text;
using Franchise.Domain.Errors;

namespace Franchise.Domain.Entities;

/// <summary>
/// A Good Food restaurant (franchise). This is the tenant every other service
/// scopes its data by: its Id is the tenant_id carried in JWTs and referenced
/// by menus, stock and orders.
/// </summary>
public sealed class Restaurant
{
    private readonly List<Supplier> _suppliers = [];

    private Restaurant() { } // EF Core

    public Restaurant(string name, string slug, string address, string city, string plan = "free")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw DomainException.Validation("Restaurant name is required.");
        }

        Id = Guid.NewGuid();
        Name = name.Trim();
        Slug = string.IsNullOrWhiteSpace(slug) ? GenerateSlug(name) : slug.Trim().ToLowerInvariant();
        Address = address?.Trim() ?? string.Empty;
        City = city?.Trim() ?? string.Empty;
        Plan = plan;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Rehydrates a restaurant with a known id — used by the one-off
    /// import from auth-service so existing tenant ids (referenced by orders,
    /// stock and menus) are preserved.</summary>
    public static Restaurant Import(Guid id, string name, string slug, bool isActive, string plan)
    {
        var restaurant = new Restaurant(name, slug, string.Empty, string.Empty, plan)
        {
            Id = id,
            IsActive = isActive,
        };
        return restaurant;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string Plan { get; private set; } = "free";

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<Supplier> Suppliers => _suppliers.AsReadOnly();

    public void Update(string name, string address, string city, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw DomainException.Validation("Restaurant name is required.");
        }

        Name = name.Trim();
        Address = address?.Trim() ?? string.Empty;
        City = city?.Trim() ?? string.Empty;
        IsActive = isActive;
    }

    /// <summary>Adds a supplier to this franchise (tenant boundary enforced here).</summary>
    public Supplier AddSupplier(string name, string contactName, string email, string phone)
    {
        var supplier = new Supplier(Id, name, contactName, email, phone);
        _suppliers.Add(supplier);
        return supplier;
    }

    /// <summary>URL-friendly slug: lowercased, accents stripped, spaces to dashes.</summary>
    public static string GenerateSlug(string value)
    {
        // Decompose accented characters so diacritics can be dropped (é → e).
        var normalized = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is >= 'a' and <= 'z' || c is >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (c is ' ' or '-' or '_')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }
}
