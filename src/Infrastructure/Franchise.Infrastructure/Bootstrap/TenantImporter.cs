using System.Net.Http.Json;
using Franchise.Domain.Repositories;
using Microsoft.Extensions.Logging;
using RestaurantEntity = Franchise.Domain.Entities.Restaurant;

namespace Franchise.Infrastructure.Bootstrap;

/// <summary>
/// One-off migration: restaurants used to live in auth-service as "tenants".
/// Their ids are referenced by orders, stock and menus, so on first boot we
/// import them <b>preserving the original ids</b>. Once imported, auth-service
/// no longer owns restaurants. Falls back to seeding defaults if auth-service
/// has nothing to offer.
/// </summary>
public sealed class TenantImporter(
    IRestaurantRepository restaurants,
    IUnitOfWork uow,
    HttpClient http,
    ILogger<TenantImporter> logger)
{
    private sealed record LegacyTenant(Guid Id, string Name, string Slug, bool Is_Active, string? Plan);

    private sealed record LegacyTenantsResponse(List<LegacyTenant> Data);

    private static readonly (string Name, string Slug, string Address, string City)[] Defaults =
    [
        ("Good Food République", "default", "12 place de la République", "Paris"),
        ("Good Food Montparnasse", "montparnasse", "5 rue du Départ", "Paris"),
    ];

    public async Task ImportAsync(string authServiceUrl, CancellationToken ct = default)
    {
        if (await restaurants.CountAsync(ct) > 0)
        {
            return; // already populated
        }

        var imported = await TryImportFromAuthAsync(authServiceUrl, ct);
        if (imported > 0)
        {
            logger.LogInformation("Imported {Count} restaurants from auth-service (ids preserved)", imported);
            return;
        }

        foreach (var (name, slug, address, city) in Defaults)
        {
            await restaurants.AddAsync(new RestaurantEntity(name, slug, address, city), ct);
        }

        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} default restaurants", Defaults.Length);
    }

    private async Task<int> TryImportFromAuthAsync(string authServiceUrl, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                var response = await http.GetFromJsonAsync<LegacyTenantsResponse>(
                    $"{authServiceUrl}/api/tenants", ct);

                var tenants = response?.Data ?? [];
                if (tenants.Count == 0)
                {
                    return 0;
                }

                foreach (var t in tenants)
                {
                    await restaurants.AddAsync(
                        RestaurantEntity.Import(t.Id, t.Name, t.Slug, t.Is_Active, t.Plan ?? "free"), ct);
                }

                await uow.SaveChangesAsync(ct);
                return tenants.Count;
            }
            catch (Exception ex)
            {
                logger.LogWarning("auth-service not reachable for import (attempt {Attempt}/5): {Error}",
                    attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }

        return 0;
    }
}
