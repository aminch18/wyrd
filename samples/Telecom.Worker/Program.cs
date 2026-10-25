using Microsoft.EntityFrameworkCore;
using Telecom.Hosting;
using Wyrd.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTelecom(builder.Configuration);
builder.Services.AddHostedService<TelecomWorker>();
using var host = builder.Build();
if (args.Contains("--init", StringComparer.Ordinal))
{
    // Disposable local sample databases only. Production schema upgrades need reviewed migrations.
    var factory = host.Services.GetRequiredService<IDbContextFactory<TelecomDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
    return;
}
await host.RunAsync();

internal sealed class TelecomWorker(EfCoreRuntime<TelecomDbContext> runtime) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => runtime.RunAsync(stoppingToken);
}
