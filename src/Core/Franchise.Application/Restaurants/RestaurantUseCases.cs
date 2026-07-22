using FluentValidation;
using Franchise.Application.Common;
using Franchise.Domain.Errors;
using Franchise.Domain.Repositories;
using MediatR;
using RestaurantEntity = Franchise.Domain.Entities.Restaurant;

namespace Franchise.Application.Restaurants;

// ── Queries (public: the storefront lists restaurants) ──────

public sealed record GetRestaurantsQuery(bool OnlyActive = true) : IRequest<IReadOnlyList<RestaurantDto>>;

public sealed class GetRestaurantsHandler(IRestaurantRepository restaurants)
    : IRequestHandler<GetRestaurantsQuery, IReadOnlyList<RestaurantDto>>
{
    public async Task<IReadOnlyList<RestaurantDto>> Handle(GetRestaurantsQuery query, CancellationToken ct)
    {
        var list = await restaurants.ListAsync(query.OnlyActive, ct);
        return list.Select(r => r.ToDto()).ToList();
    }
}

public sealed record GetRestaurantByIdQuery(Guid Id) : IRequest<RestaurantDto>;

public sealed class GetRestaurantByIdHandler(IRestaurantRepository restaurants)
    : IRequestHandler<GetRestaurantByIdQuery, RestaurantDto>
{
    public async Task<RestaurantDto> Handle(GetRestaurantByIdQuery query, CancellationToken ct)
    {
        var restaurant = await restaurants.GetByIdAsync(query.Id, ct)
            ?? throw DomainException.NotFound("Restaurant not found.");
        return restaurant.ToDto();
    }
}

// ── Commands (head office manages the franchise network) ────

public sealed record CreateRestaurantCommand(
    CurrentUser User,
    string Name,
    string Slug,
    string Address,
    string City,
    string Plan) : IRequest<RestaurantDto>;

public sealed class CreateRestaurantValidator : AbstractValidator<CreateRestaurantCommand>
{
    public CreateRestaurantValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateRestaurantHandler(IRestaurantRepository restaurants, IUnitOfWork uow)
    : IRequestHandler<CreateRestaurantCommand, RestaurantDto>
{
    public async Task<RestaurantDto> Handle(CreateRestaurantCommand cmd, CancellationToken ct)
    {
        if (!cmd.User.IsAdmin)
        {
            throw DomainException.Forbidden("Only head office can create a restaurant.");
        }

        var slug = string.IsNullOrWhiteSpace(cmd.Slug) ? RestaurantEntity.GenerateSlug(cmd.Name) : cmd.Slug;
        if (await restaurants.GetBySlugAsync(slug, ct) is not null)
        {
            throw DomainException.Conflict($"A restaurant with slug '{slug}' already exists.");
        }

        var restaurant = new RestaurantEntity(cmd.Name, slug, cmd.Address, cmd.City, cmd.Plan ?? "free");
        await restaurants.AddAsync(restaurant, ct);
        await uow.SaveChangesAsync(ct);
        return restaurant.ToDto();
    }
}

public sealed record UpdateRestaurantCommand(
    CurrentUser User,
    Guid Id,
    string Name,
    string Address,
    string City,
    bool IsActive) : IRequest<RestaurantDto>;

public sealed class UpdateRestaurantHandler(IRestaurantRepository restaurants, IUnitOfWork uow)
    : IRequestHandler<UpdateRestaurantCommand, RestaurantDto>
{
    public async Task<RestaurantDto> Handle(UpdateRestaurantCommand cmd, CancellationToken ct)
    {
        var restaurant = await restaurants.GetByIdAsync(cmd.Id, ct)
            ?? throw DomainException.NotFound("Restaurant not found.");

        // Head office may edit any restaurant; a manager only their own.
        if (!cmd.User.IsAdmin && cmd.User.RequireOwnRestaurant() != restaurant.Id)
        {
            throw DomainException.Forbidden("You can only edit your own restaurant.");
        }

        restaurant.Update(cmd.Name, cmd.Address, cmd.City, cmd.IsActive);
        await uow.SaveChangesAsync(ct);
        return restaurant.ToDto();
    }
}
