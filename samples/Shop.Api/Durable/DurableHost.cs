using Microsoft.EntityFrameworkCore;
using Wyrd;
using Wyrd.EntityFrameworkCore;
using Wyrd.Runtime;

namespace Shop.Api.Durable;

public static class DurableHost
{
    /// <summary>How long the shop keeps what Wyrd stores: each line is a decision, made here.</summary>
    private static readonly Retention Retention = new()
    {
        Deduplication = Keep.For(7.Days),          // far longer than any redelivery
        FinishedInstances = Keep.For(30.Days),     // a month to look into an order's process, or replay it with Urd
        RelayedMessages = Keep.For(7.Days),        // only the outbox's copy: the facts stay in the events
        Events = Keep.Forever("the shop's record of what happened"),
        DeadLetters = Keep.Forever("an operator reviews each one"),
    };

    /// <summary>
    /// The shop on PostgreSQL through EF Core (Verdandi): the same module, now durable. <paramref name="cleanup"/> says
    /// who removes what expired: these workers, or a separate cleaner (<see cref="AddShopCleaner"/>).
    /// </summary>
    public static void AddDurableShop(this IServiceCollection services, string connectionString, CleanupMode cleanup)
    {
        services.AddRuntime(connectionString, new EfCoreRuntimeOptions { Cleanup = cleanup });
        services.AddSingleton<IWyrdRuntime>(provider => provider.GetRequiredService<EfCoreRuntime<ShopDbContext>>());
        services.AddHostedService<WyrdWorker>();
    }

    /// <summary>The shop's cleaner on its own: no HTTP and no workers, only retention.</summary>
    public static void AddShopCleaner(this IServiceCollection services, string connectionString)
    {
        services.AddRuntime(connectionString, new EfCoreRuntimeOptions { Cleanup = CleanupMode.Separate });
        services.AddHostedService<WyrdCleaner>();
    }

    private static void AddRuntime(this IServiceCollection services, string connectionString, EfCoreRuntimeOptions options)
    {
        services.AddDbContextFactory<ShopDbContext>(builder => builder.UseNpgsql(connectionString));
        services.AddSingleton(provider => ShopModule.Register(new EfCoreRuntime<ShopDbContext>(
            provider.GetRequiredService<IDbContextFactory<ShopDbContext>>(),
            ShopJson.Default,
            Retention,
            options,
            logger: provider.GetRequiredService<ILogger<EfCoreRuntime<ShopDbContext>>>())));
    }
}
