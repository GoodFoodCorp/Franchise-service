using Franchise.Application.Restaurants;
using MediatR;

namespace Franchise.Api;

/// <summary>Locates, once at startup, the restaurants still without
/// coordinates. Idempotent: running on several replicas only repeats a no-op.</summary>
public sealed class RestaurantLocationBackfill(IServiceScopeFactory scopes, ILogger<RestaurantLocationBackfill> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var located = await mediator.Send(new LocateUnplacedRestaurantsCommand(TimeSpan.FromSeconds(1.1)), stoppingToken);
            if (located > 0)
            {
                logger.LogInformation("Located {Count} restaurant(s) without coordinates", located);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Never fatal: addresses are located again on their next update.
            logger.LogWarning(ex, "Restaurant location backfill failed");
        }
    }
}
