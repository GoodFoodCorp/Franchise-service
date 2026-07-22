using FluentValidation;
using Franchise.Application.Common;
using Franchise.Domain.Errors;
using Franchise.Domain.Repositories;
using MediatR;

namespace Franchise.Application.Suppliers;

// Suppliers are always scoped to the manager's own franchise.

public sealed record GetSuppliersQuery(CurrentUser User) : IRequest<IReadOnlyList<SupplierDto>>;

public sealed class GetSuppliersHandler(ISupplierRepository suppliers)
    : IRequestHandler<GetSuppliersQuery, IReadOnlyList<SupplierDto>>
{
    public async Task<IReadOnlyList<SupplierDto>> Handle(GetSuppliersQuery query, CancellationToken ct)
    {
        var restaurantId = query.User.RequireOwnRestaurant();
        var list = await suppliers.ListByRestaurantAsync(restaurantId, ct);
        return list.Select(s => s.ToDto()).ToList();
    }
}

public sealed record CreateSupplierCommand(
    CurrentUser User,
    string Name,
    string ContactName,
    string Email,
    string Phone) : IRequest<SupplierDto>;

public sealed class CreateSupplierValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Email).EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.Email));
    }
}

public sealed class CreateSupplierHandler(
    IRestaurantRepository restaurants,
    IUnitOfWork uow) : IRequestHandler<CreateSupplierCommand, SupplierDto>
{
    public async Task<SupplierDto> Handle(CreateSupplierCommand cmd, CancellationToken ct)
    {
        var restaurantId = cmd.User.RequireOwnRestaurant();
        var restaurant = await restaurants.GetByIdAsync(restaurantId, ct)
            ?? throw DomainException.NotFound("Your restaurant no longer exists.");

        var supplier = restaurant.AddSupplier(cmd.Name, cmd.ContactName, cmd.Email, cmd.Phone);
        await uow.SaveChangesAsync(ct);
        return supplier.ToDto();
    }
}

public sealed record DeleteSupplierCommand(CurrentUser User, Guid Id) : IRequest;

public sealed class DeleteSupplierHandler(ISupplierRepository suppliers, IUnitOfWork uow)
    : IRequestHandler<DeleteSupplierCommand>
{
    public async Task Handle(DeleteSupplierCommand cmd, CancellationToken ct)
    {
        var restaurantId = cmd.User.RequireOwnRestaurant();
        var supplier = await suppliers.GetByIdAsync(cmd.Id, ct)
            ?? throw DomainException.NotFound("Supplier not found.");

        if (!supplier.IsOwnedBy(restaurantId))
        {
            throw DomainException.Forbidden("This supplier belongs to another restaurant.");
        }

        await suppliers.RemoveAsync(supplier, ct);
        await uow.SaveChangesAsync(ct);
    }
}
