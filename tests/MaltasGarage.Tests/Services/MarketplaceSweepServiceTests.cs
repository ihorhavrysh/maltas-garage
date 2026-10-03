using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Tests.Services;

public class MarketplaceSweepServiceTests
{
    /// <summary>Fails the first sweep (a paused database), then succeeds.</summary>
    private sealed class FlakyLifecycle : IListingLifecycleService
    {
        private int _calls;
        public int Calls => _calls;
        public TaskCompletionSource SecondSweep { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnsureCurrentAsync(Guid listingId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LifecycleSweepResult> SweepAsync(CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1)
                throw new InvalidOperationException("Database is paused");
            SecondSweep.TrySetResult();
            return Task.FromResult(new LifecycleSweepResult(0, 0, 0, 0, 0));
        }
    }

    [Fact]
    public async Task FailingSweep_DoesNotStopTheTimer()
    {
        var lifecycle = new FlakyLifecycle();
        var services = new ServiceCollection()
            .AddSingleton<IListingLifecycleService>(lifecycle)
            .AddSingleton(Options.Create(new DemoSettings { Enabled = false }))
            .BuildServiceProvider();

        var sweep = new MarketplaceSweepService(services, TimeSpan.FromMilliseconds(20),
            NullLogger<MarketplaceSweepService>.Instance);

        await sweep.StartAsync(CancellationToken.None);
        var finished = await Task.WhenAny(lifecycle.SecondSweep.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await sweep.StopAsync(CancellationToken.None);

        Assert.Same(lifecycle.SecondSweep.Task, finished);
        Assert.True(lifecycle.Calls >= 2);
    }

    [Fact]
    public void Interval_ComesFromSettings()
    {
        var sweep = new MarketplaceSweepService(new ServiceCollection().BuildServiceProvider(),
            Options.Create(new AppSettings { SweepIntervalMinutes = 7 }), NullLogger<MarketplaceSweepService>.Instance);

        Assert.Equal(TimeSpan.FromMinutes(7), sweep.Interval);
    }
}
