using Franchise.Api.Auth;
using Franchise.Application.Suppliers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Franchise.Api.Controllers;

/// <summary>Suppliers of the franchisee's own restaurant.</summary>
[ApiController]
[Route("api/franchise/suppliers")]
[Authorize(Roles = "manager,admin")]
public sealed class SuppliersController(IMediator mediator) : ControllerBase
{
    public sealed record CreateBody(string Name, string ContactName, string Email, string Phone);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await mediator.Send(new GetSuppliersQuery(CurrentUserFactory.FromClaims(User)), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBody body, CancellationToken ct)
    {
        var dto = await mediator.Send(new CreateSupplierCommand(
            CurrentUserFactory.FromClaims(User), body.Name, body.ContactName, body.Email, body.Phone), ct);
        return Created($"/api/franchise/suppliers/{dto.Id}", dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await mediator.Send(new DeleteSupplierCommand(CurrentUserFactory.FromClaims(User), id), ct);
        return NoContent();
    }
}
