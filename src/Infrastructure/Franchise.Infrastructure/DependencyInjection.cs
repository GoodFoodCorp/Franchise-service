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

        var userAgent = configuration["GEOCODER_USER_AGENT"] ?? "GoodFood/1.0 (contact@goodfood.fr)";
        services.AddSingleton(ServedCountries.FromSetting(configuration["GEOCODER_COUNTRIES"]));

        var suggestUrl = configuration["ADDRESS_SUGGEST_URL"] ?? "https://photon.komoot.io/";
        if (suggestUrl == "disabled")
        {
            services.AddSingleton<IAddressSuggester, DisabledAddressSuggester>();
        }
        else
        {
            services.AddHttpClient<IAddressSuggester, PhotonAddressSuggester>(c =>
            {
                c.BaseAddress = new Uri(suggestUrl.EndsWith('/') ? suggestUrl : suggestUrl + "/");
                c.Timeout = TimeSpan.FromSeconds(3);
                c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            });
        }

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
                c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            });
        }

        return services;
    }
}
