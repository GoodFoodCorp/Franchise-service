using Franchise.Api.Auth;
using Franchise.Application.Restaurants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Franchise.Api.Controllers;

/// <summary>Restaurants (franchises). The list is public — the storefront shows
/// it before login; management is head-office / franchisee only.</summary>
[ApiController]
[Route("api/restaurants")]
public sealed class RestaurantsController(IMediator mediator) : ControllerBase
{
    public sealed record CreateBody(string Name, string Slug, string Address, string City, string Plan);

    public sealed record UpdateBody(string Name, string Address, string City, bool IsActive);

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        Ok(await mediator.Send(new GetRestaurantsQuery(!includeInactive), ct));

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await mediator.Send(new GetRestaurantByIdQuery(id), ct));

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
}
