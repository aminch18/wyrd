using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Telecom;
using Telecom.Infrastructure;
using Wyrd.EntityFrameworkCore;
using Wyrd.Runtime;

namespace Wyrd.Benchmarks.Load;

/// <summary>
/// The Telecom sample on the durable host, with providers that answer after <c>--provider-latency</c> milliseconds:
/// three provider calls (capture the payment, submit the port, provision the eSIM) stand between a contract and a
/// provisioned subscription, so the workers' handling of slow third parties is what this measures.
/// </summary>
internal static class TelecomHost
{
    public static IDbContextFactory<TelecomDbContext> Contexts(string connection, int maxPool = 100) =>
        new PooledDbContextFactory<TelecomDbContext>(new DbContextOptionsBuilder<TelecomDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { MaxPoolSize = maxPool }.ConnectionString).Options);

    public static EfCoreRuntime<TelecomDbContext> Runtime(IDbContextFactory<TelecomDbContext> contexts, TimeSpan providerLatency, Retention? retention = null) =>
        TelecomModule.Register(
            new EfCoreRuntime<TelecomDbContext>(contexts, TelecomJson.Default, retention ?? ShopHost.KeepEverything),
            new SlowProviders(providerLatency));

    /// <summary>The sample's deterministic providers, each reply after <paramref name="latency"/>.</summary>
    private sealed class SlowProviders(TimeSpan latency) : IProviderApi
    {
        private readonly DeterministicProviders _providers = new();

        public async Task<ProviderReply> CallAsync(ProviderRequest request, Guid deliveryKey, CancellationToken cancellationToken)
        {
            await Task.Delay(latency, cancellationToken);
            return await _providers.CallAsync(request, deliveryKey, cancellationToken);
        }
    }
}
