using Microsoft.EntityFrameworkCore;
using Wyrd.EntityFrameworkCore;
using Wyrd.Runtime;

namespace Shop.Api.Durable;

public static class DurableHost
{
    /// <summary>The shop on PostgreSQL through EF Core (Verdandi): the same module, now durable.</summary>
    public static void AddDurableShop(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<ShopDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton(provider => ShopModule.Register(new EfCoreRuntime<ShopDbContext>(
            provider.GetRequiredService<IDbContextFactory<ShopDbContext>>(),
            ShopJson.Default,
            logger: provider.GetRequiredService<ILogger<EfCoreRuntime<ShopDbContext>>>())));
        services.AddSingleton<IWyrdRuntime>(provider => provider.GetRequiredService<EfCoreRuntime<ShopDbContext>>());
        services.AddHostedService<WyrdWorker>();
    }
}
