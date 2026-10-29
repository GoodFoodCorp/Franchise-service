using Franchise.Application.Common;
using Franchise.Domain.Repositories;
using Franchise.Infrastructure.Bootstrap;
using Franchise.Infrastructure.Geocoding;
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
        services.AddSingleton<IClock, SystemClock>();
        services.AddMemoryCache();

        var geocoderUrl = configuration["GEOCODER_URL"] ?? "https://nominatim.openstreetmap.org/";
        if (geocoderUrl == "disabled")
        {
            services.AddSingleton<IGeocoder, DisabledGeocoder>();
        }
        else
        {
            services.AddHttpClient<IGeocoder, NominatimGeocoder>(c =>
            {
                c.BaseAddress = new Uri(geocoderUrl.EndsWith('/') ? geocoderUrl : geocoderUrl + "/");
                c.Timeout = TimeSpan.FromSeconds(3);
                // Nominatim's usage policy requires an identifying User-Agent.
                c.DefaultRequestHeaders.UserAgent.ParseAdd(configuration["GEOCODER_USER_AGENT"] ?? "GoodFood/1.0 (contact@goodfood.fr)");
            });
        }

        return services;
    }
}
