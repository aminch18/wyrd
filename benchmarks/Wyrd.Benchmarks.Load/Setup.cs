using Microsoft.EntityFrameworkCore;
using Shop.Inventory;

namespace Wyrd.Benchmarks.Load;

/// <summary>A run's database: Wyrd's tables, statement statistics, and stock for <c>--skus</c> SKUs.</summary>
internal static class Setup
{
    public static async Task<int> RunAsync(string connection, Options options)
    {
        var skus = options.Number("skus", 1_000);
        var contexts = ShopHost.Contexts(connection);
        await using (var db = await contexts.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_stat_statements");
        }

        using var shop = ShopHost.Runtime(contexts);
        for (var index = 0; index < skus; index++)
        {
            await shop.SendAsync(new Restock(ShopHost.Sku(index), ShopHost.StockPerSku));
        }

        // Statistics from here on are the run's, not the setup's.
        await using (var db = await contexts.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_statements_reset()");
        }

        Console.WriteLine($"Set up: Wyrd's tables, {skus:N0} SKU(s) in stock.");
        return 0;
    }
}
