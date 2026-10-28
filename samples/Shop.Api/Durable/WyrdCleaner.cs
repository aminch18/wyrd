using Wyrd.EntityFrameworkCore;

namespace Shop.Api.Durable;

/// <summary>
/// The shop's cleaner, a service of its own (<c>Shop.Api cleanup</c>): removes what the retention no longer keeps, so the
/// API's workers only handle messages. Run one; the API runs with <c>Wyrd__Cleanup=Separate</c>.
/// </summary>
public sealed class WyrdCleaner(EfCoreRuntime<ShopDbContext> wyrd) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => wyrd.RunCleanupAsync(stoppingToken);
}
