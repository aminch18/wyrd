using Wyrd.Runtime;

namespace Shop.Api;

/// <summary>Moves Wyrd's clock with real time, so timeouts and retries fire while the API runs.</summary>
public sealed class WyrdClock(InMemoryRuntime wyrd) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await wyrd.AdvanceAsync(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
