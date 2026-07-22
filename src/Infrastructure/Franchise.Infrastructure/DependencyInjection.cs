using Franchise.Domain.Repositories;
using Franchise.Infrastructure.Bootstrap;
using Franchise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Franchise.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FranchiseDb")
            ?? throw new InvalidOperationException("ConnectionStrings__FranchiseDb is required.");

        services.AddDbContext<FranchiseDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IRestaurantRepository, RestaurantRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddHttpClient<TenantImporter>();
        return services;
    }
}
