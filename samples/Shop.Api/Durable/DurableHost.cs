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

    /// <summary>The shop on PostgreSQL through EF Core (Verdandi): the same module, now durable.</summary>
    public static void AddDurableShop(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<ShopDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton(provider => ShopModule.Register(new EfCoreRuntime<ShopDbContext>(
            provider.GetRequiredService<IDbContextFactory<ShopDbContext>>(),
            ShopJson.Default,
            Retention,
            logger: provider.GetRequiredService<ILogger<EfCoreRuntime<ShopDbContext>>>())));
        services.AddSingleton<IWyrdRuntime>(provider => provider.GetRequiredService<EfCoreRuntime<ShopDbContext>>());
        services.AddHostedService<WyrdWorker>();
    }
}
