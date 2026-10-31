using Franchise.Api.Auth;
using Franchise.Application.Common;
using Franchise.Application.Restaurants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Franchise.Api.Controllers;

/// <summary>Restaurants (franchises). The list is public — the storefront shows
/// it before login; management is head-office / franchisee only.</summary>
[ApiController]
[Route("api/restaurants")]
public sealed class RestaurantsController(IMediator mediator) : ControllerBase
{
    public sealed record CreateBody(string Name, string Slug, string Address, string City, string Plan);

    public sealed record UpdateBody(string Name, string Address, string City, bool IsActive);

    public sealed record OperationsBody(
        string Phone,
        string TimeZone,
        IReadOnlyList<OpeningSlotDto> OpeningHours,
        int PrepTimeMinutes,
        double DeliveryRadiusKm,
        long DeliveryFeeCents,
        long MinimumOrderCents,
        int SeatingCapacity);

    public sealed record OrderIntakeBody(bool AcceptingOrders);

    /// <summary>Active restaurants; head office may add <c>includeInactive=true</c>.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        CurrentUser? user = User.Identity?.IsAuthenticated == true ? CurrentUserFactory.FromClaims(User) : null;
        Response.Headers.CacheControl = "public, max-age=30";
        return Ok(await mediator.Send(new GetRestaurantsQuery(includeInactive, user), ct));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=30";
        return Ok(await mediator.Send(new GetRestaurantByIdQuery(id), ct));
    }

    /// <summary>Can the restaurant deliver this address now, for what fee and
    /// in how long? Shown in the cart before checkout.</summary>
    /// <summary>Address autocomplete (EU street addresses). lat/lng, e.g. the
    /// restaurant's, rank nearby addresses first; places=true adds towns and
    /// postal codes. Rate limited per client.</summary>
    [HttpGet("address-suggestions")]
    [AllowAnonymous]
    [EnableRateLimiting(AddressSuggestionsPolicy)]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> SuggestAddresses(
        [FromQuery] string? q, [FromQuery] double? lat, [FromQuery] double? lng, [FromQuery] int limit = 5,
        [FromQuery] bool places = false, CancellationToken ct = default) =>
        Ok(await mediator.Send(new SuggestAddressesQuery(q, lat, lng, limit, places), ct));

    public const string AddressSuggestionsPolicy = "address-suggestions";

    [HttpGet("{id:guid}/quote")]
    [AllowAnonymous]
    public async Task<IActionResult> Quote(Guid id, [FromQuery] string? address, [FromQuery] double? lat, [FromQuery] double? lng, CancellationToken ct) =>
        Ok(await mediator.Send(new QuoteDeliveryQuery(id, address, lat, lng), ct));

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] CreateBody body, CancellationToken ct)
    {
        var dto = await mediator.Send(new CreateRestaurantCommand(
            CurrentUserFactory.FromClaims(User), body.Name, body.Slug, body.Address, body.City, body.Plan), ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBody body, CancellationToken ct) =>
        Ok(await mediator.Send(new UpdateRestaurantCommand(
            CurrentUserFactory.FromClaims(User), id, body.Name, body.Address, body.City, body.IsActive), ct));

    /// <summary>Opening hours, delivery zone and fees, preparation time,
    /// seating capacity.</summary>
    [HttpPut("{id:guid}/operations")]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> UpdateOperations(Guid id, [FromBody] OperationsBody body, CancellationToken ct) =>
        Ok(await mediator.Send(new UpdateOperationsCommand(
            CurrentUserFactory.FromClaims(User), id, body.Phone, body.TimeZone, body.OpeningHours ?? [],
            body.PrepTimeMinutes, body.DeliveryRadiusKm, body.DeliveryFeeCents, body.MinimumOrderCents,
            body.SeatingCapacity), ct));

    /// <summary>Pauses or resumes new orders.</summary>
    [HttpPut("{id:guid}/order-intake")]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> SetOrderIntake(Guid id, [FromBody] OrderIntakeBody body, CancellationToken ct) =>
        Ok(await mediator.Send(new SetOrderIntakeCommand(CurrentUserFactory.FromClaims(User), id, body.AcceptingOrders), ct));
}

/// <summary>Service-to-service API (order-service), never exposed by the
/// ingress (which only routes /api/...).</summary>
[ApiController]
[Route("internal/restaurants")]
[Franchise.Api.Auth.ServiceKey]
public sealed class InternalRestaurantsController(IMediator mediator) : ControllerBase
{
    public sealed record QuoteBody(string? Address, double? Latitude, double? Longitude);

    [HttpPost("{id:guid}/quote")]
    public async Task<IActionResult> Quote(Guid id, [FromBody] QuoteBody body, CancellationToken ct) =>
        Ok(await mediator.Send(new QuoteDeliveryQuery(id, body.Address, body.Latitude, body.Longitude), ct));
}
