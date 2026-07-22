using System.Security.Claims;
using Franchise.Application.Common;

namespace Franchise.Api.Auth;

public static class CurrentUserFactory
{
    /// <summary>Builds the application CurrentUser from auth-service JWT claims
    /// (sub, tenant_id = the manager's restaurant, role_slugs).</summary>
    public static CurrentUser FromClaims(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? string.Empty;

        Guid? restaurantId = null;
        if (Guid.TryParse(principal.FindFirstValue("tenant_id"), out var parsed))
        {
            restaurantId = parsed;
        }

        var roles = principal.FindAll("role_slugs")
            .Select(c => c.Value.ToLowerInvariant())
            .Distinct()
            .ToList();

        return new CurrentUser(userId, restaurantId, roles);
    }
}
