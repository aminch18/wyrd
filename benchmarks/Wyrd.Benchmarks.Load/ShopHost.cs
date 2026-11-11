using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Shop;
using Wyrd.EntityFrameworkCore;
using Wyrd.Runtime;

namespace Wyrd.Benchmarks.Load;

/// <summary>The Shop on the durable host, configured the same in every role.</summary>
internal static class ShopHost
{
    /// <summary>The SKUs of the stock: <c>SKU-0</c> to <c>SKU-(n−1)</c>. One SKU is a hot key: every order writes it.</summary>
    public static string Sku(int index) => "SKU-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Enough stock that no order of a run is rejected for lack of it.</summary>
    public const int StockPerSku = 1_000_000;

    /// <summary>Every run keeps everything: it is short, and its database is dropped (the soak run sets its own).</summary>
    public static readonly Retention KeepEverything = new()
    {
        Deduplication = Keep.Forever("a benchmark run is short, and its database is dropped"),
        FinishedInstances = Keep.Forever("a benchmark run is short, and its database is dropped"),
        RelayedMessages = Keep.Forever("a benchmark run is short, and its database is dropped"),
        Events = Keep.Forever("a benchmark run is short, and its database is dropped"),
        DeadLetters = Keep.Forever("a benchmark run is short, and its database is dropped"),
    };

    /// <summary>Pooled contexts, as an application registers them (<c>AddPooledDbContextFactory</c>).</summary>
    public static IDbContextFactory<WyrdDbContext> Contexts(string connection, int maxPool = 100) =>
        new PooledDbContextFactory<WyrdDbContext>(new DbContextOptionsBuilder<WyrdDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { MaxPoolSize = maxPool }.ConnectionString).Options);

    public static EfCoreRuntime<WyrdDbContext> Runtime(IDbContextFactory<WyrdDbContext> contexts) =>
        ShopModule.Register(new EfCoreRuntime<WyrdDbContext>(contexts, ShopJson.Default, KeepEverything));
}
