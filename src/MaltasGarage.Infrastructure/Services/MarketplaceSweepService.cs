using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Infrastructure.Data.Seed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Infrastructure.Services;

/// <summary>
/// The only background timer. It replaces four polling services (auction activation,
/// auction completion, offer expiry, escrow release) that used to wake up every 5 to 60
/// minutes each.
///
/// It is a catch-up mechanism, not the source of correctness: the first sweep runs as soon as
/// the app starts, which on the free App Service tier is also when it wakes up from idling,
/// and pages that act on a listing evaluate it on read. After that it runs every
/// <see cref="Interval"/> while the app stays awake, which keeps CPU use well inside the
/// free tier's daily quota.
/// </summary>
public class MarketplaceSweepService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarketplaceSweepService> _logger;

    /// <summary>Time between sweeps, from <c>App:SweepIntervalMinutes</c> (15 by default).</summary>
    public TimeSpan Interval { get; }

    public MarketplaceSweepService(IServiceProvider serviceProvider, IOptions<AppSettings> settings,
        ILogger<MarketplaceSweepService> logger)
        : this(serviceProvider, TimeSpan.FromMinutes(settings.Value.SweepIntervalMinutes), logger)
    {
    }

    // Tests pass a short interval directly
    public MarketplaceSweepService(IServiceProvider serviceProvider, TimeSpan interval, ILogger<MarketplaceSweepService> logger)
    {
        _serviceProvider = serviceProvider;
        Interval = interval;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await SweepOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var services = scope.ServiceProvider;

            if (services.GetRequiredService<IOptions<DemoSettings>>().Value.Enabled)
            {
                // A due reset reseeds everything, so the lifecycle sweep has nothing left to do
                if (await services.GetRequiredService<DemoResetService>().ResetIfDueAsync())
                    return;
                await services.GetRequiredService<DemoDataSeeder>().RefreshShowcaseAsync();
            }

            await services.GetRequiredService<IListingLifecycleService>().SweepAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A sleeping or paused database must not kill the timer; the next tick retries
            _logger.LogError(ex, "Marketplace sweep failed");
        }
    }
}
