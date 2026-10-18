using Wyrd.PostgreSql;

namespace Shop.Api;

/// <summary>Creates Wyrd's tables on start, then handles queued messages, timers and retries until shutdown.</summary>
public sealed class WyrdWorker(PostgresRuntime wyrd) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await wyrd.EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => wyrd.RunAsync(stoppingToken);
}
