using Franchise.Domain.Errors;

namespace Franchise.Application.Common;

/// <summary>Authenticated caller built from JWT claims by the API layer.</summary>
public sealed record CurrentUser(string UserId, Guid? RestaurantId, IReadOnlyList<string> Roles)
{
    public bool IsAdmin => Roles.Contains("admin");

    public bool IsManager => Roles.Contains("manager");

    /// <summary>The franchise a manager may act on — their own, always.</summary>
    public Guid RequireOwnRestaurant()
    {
        if (!IsManager)
        {
            throw DomainException.Forbidden("Only a franchise manager can do this.");
        }

        if (RestaurantId is null || RestaurantId == Guid.Empty)
        {
            throw DomainException.Forbidden("Your account is not linked to a restaurant.");
        }

        return RestaurantId.Value;
    }
}
