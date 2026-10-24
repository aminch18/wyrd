using Microsoft.EntityFrameworkCore;
using Wyrd.EntityFrameworkCore;

namespace Shop.Api.Durable;

/// <summary>Applies the shop's migrations on start, then handles queued messages, timers and retries until shutdown.</summary>
public sealed class WyrdWorker(EfCoreRuntime<ShopDbContext> wyrd, IDbContextFactory<ShopDbContext> contexts) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Fine for a sample. In production, apply migrations as a deployment step (a bundle or a script) instead.
        await using (var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => wyrd.RunAsync(stoppingToken);
}
