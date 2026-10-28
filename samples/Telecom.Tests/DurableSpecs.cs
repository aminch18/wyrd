using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Telecom.Infrastructure;
using Testcontainers.PostgreSql;
using Wyrd;
using Wyrd.EntityFrameworkCore;
using Wyrd.Runtime;

namespace Telecom.Tests;

public sealed class TelecomDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _server = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private int _database;
    public Task InitializeAsync() => _server.StartAsync();
    public Task DisposeAsync() => _server.DisposeAsync().AsTask();
    public async Task<IDbContextFactory<TelecomDbContext>> NewAsync()
    {
        var connection = new NpgsqlConnectionStringBuilder(_server.GetConnectionString()) { Database = $"telecom_{Interlocked.Increment(ref _database)}" }.ConnectionString;
        var options = new DbContextOptionsBuilder<TelecomDbContext>().UseNpgsql(connection).Options;
        var factory = new Contexts(options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        return factory;
    }
    private sealed class Contexts(DbContextOptions<TelecomDbContext> options) : IDbContextFactory<TelecomDbContext>
    {
        public TelecomDbContext CreateDbContext() => new(options);
    }
}

[Trait("Category", "Containers")]
public sealed class DurableSpecs(TelecomDatabase database) : IClassFixture<TelecomDatabase>
{
    private static EfCoreRuntime<TelecomDbContext> Runtime(IDbContextFactory<TelecomDbContext> contexts, IProviderApi provider, TimeProvider? time = null) =>
        TelecomModule.Register(new EfCoreRuntime<TelecomDbContext>(contexts, TelecomJson.Default, DurableTelecom.Retention,
            new EfCoreRuntimeOptions { IntegrationLease = 1.Minutes }, time), provider);

    private static async Task Drain(params EfCoreRuntime<TelecomDbContext>[] workers)
    {
        // Multiple runtime instances share only the application's database and the outside providers.
        for (var pass = 0; pass < 100; pass++)
        {
            var processed = await Task.WhenAll(workers.Select(w => w.ProcessPendingAsync(10)));
            if (processed.Sum() == 0) return;
        }
        Assert.Fail("Queue did not drain within the test budget.");
    }

    [Fact]
    public async Task api_commits_only_the_initial_decision_and_workers_finish_the_process()
    {
        var factory = await database.NewAsync();
        var world = new DeterministicProviders();
        using var api = Runtime(factory, world);
        using var worker1 = Runtime(factory, world);
        using var worker2 = Runtime(factory, world);
        await api.SendAsync(new ContractSubscription(BusinessSpecs.Offer()));
        Assert.Equal(0, world.CaptureCount);
        await Drain(worker1, worker2);
        Assert.Equal(1, world.CaptureCount);
        await api.PublishAsync(new PortCompletedV1("S1", BusinessSpecs.Offer().Msisdn, 1));
        await api.PublishAsync(new ConnectivityVerifiedV1("S1", "P1", 1));
        await Drain(worker1, worker2);
        Assert.True((await api.StateOfAsync<Billing>("S1")).Started);
        Assert.True((await api.ViewAsync<PaymentBillingStatus>("S1"))!.BillingStarted);
    }

    [Fact]
    public async Task competing_workers_and_distinct_requests_cannot_both_acquire_one_number()
    {
        var factory = await database.NewAsync();
        var world = new DeterministicProviders();
        using var worker1 = Runtime(factory, world);
        using var worker2 = Runtime(factory, world);
        await Task.WhenAll(worker1.SendAsync(new ContractSubscription(BusinessSpecs.Offer())),
            worker2.SendAsync(new ContractSubscription(BusinessSpecs.Offer("S2", epoch: 2))));
        await Drain(worker1, worker2);
        var owner = (await worker1.StateOfAsync<NumberPort>(BusinessSpecs.Offer().Msisdn)).Owner!.SubscriptionId;
        var other = owner == "S1" ? "S2" : "S1";
        Assert.IsType<Subscription.Declined>(await worker1.StateOfAsync<Subscription>(other));
        Assert.Equal(1, world.CaptureCount);
    }

    [Fact]
    public async Task interrupted_worker_after_external_commit_recovers_after_lease_without_double_capture()
    {
        var factory = await database.NewAsync();
        var world = new InterruptedCapture();
        var time = new FakeTimeProvider();
        using var stopped = Runtime(factory, world, time);
        await stopped.SendAsync(new ContractSubscription(BusinessSpecs.Offer()));
        using var shutdown = new CancellationTokenSource();
        var processing = stopped.ProcessPendingAsync(100, shutdown.Token);
        await world.Committed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        // The provider acted; the worker has not recorded a reply. Cooperative stop models this boundary,
        // not an OS crash, torn write, network partition or database failover.
        shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await processing);
        Assert.Equal(1, world.World.CaptureCount);
        world.Restarted = true;
        time.Advance(2.Minutes);
        using var restarted = Runtime(factory, world, time);
        await Drain(restarted);
        Assert.Equal(1, world.World.CaptureCount);
        Assert.True((await restarted.StateOfAsync<Activation>("S1")).Paid);
    }

    [Fact]
    public async Task business_deadline_survives_restart_and_does_not_reverse_completed_port()
    {
        var factory = await database.NewAsync();
        var world = new DeterministicProviders();
        var time = new FakeTimeProvider();
        using (var first = Runtime(factory, world, time))
        {
            await first.SendAsync(new ContractSubscription(BusinessSpecs.Offer()));
            await Drain(first);
            await first.PublishAsync(new PortCompletedV1("S1", BusinessSpecs.Offer().Msisdn, 1));
            await Drain(first);
        }
        time.Advance(31.Minutes);
        using var next = Runtime(factory, world, time);
        await Drain(next);
        Assert.Equal(ProcessStatus.Compensated, await next.StatusOfAsync(Flows.ProfileProvisioning, "S1/P1/1"));
        Assert.Equal(PortPhase.Completed, (await next.StateOfAsync<NumberPort>(BusinessSpecs.Offer().Msisdn)).Phase);
        Assert.False((await next.StateOfAsync<Billing>("S1")).Started);
        Assert.Contains(await next.ViewsAsync<IncidentItem>(), i => i.Kind == "connectivity" && i.Open);
    }

    private sealed class InterruptedCapture : IProviderApi
    {
        public DeterministicProviders World { get; } = new();
        public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Restarted { get; set; }
        public async Task<ProviderReply> CallAsync(ProviderRequest request, Guid deliveryKey, CancellationToken cancellationToken)
        {
            var reply = await World.CallAsync(request, deliveryKey, cancellationToken);
            if (request.Action == ProviderAction.Capture && !Restarted)
            {
                Committed.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return reply;
        }
    }
}
