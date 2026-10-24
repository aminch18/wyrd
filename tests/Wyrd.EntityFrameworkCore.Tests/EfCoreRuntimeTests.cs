using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Shop;
using Shop.Contracts;
using Shop.Inventory;
using Shop.Orders;
using Shop.Shipping;
using Wyrd.Processes;
using Wyrd.Runtime;
using Wyrd.Testing;
using Process = Wyrd.Processes.Process;

namespace Wyrd.EntityFrameworkCore.Tests;

[Collection(PostgreSqlServer.Name)]
[Trait("Category", "Containers")]
public sealed class OnPostgreSql(PostgreSqlDatabase database) : EfCoreRuntimeTests(database);

[Collection(SqlServerServer.Name)]
[Trait("Category", "Containers")]
public sealed class OnSqlServer(SqlServerDatabase database) : EfCoreRuntimeTests(database);

/// <summary>The sample shop on a real database through EF Core: the same domain, now durable. Every test runs on each provider.</summary>
public abstract class EfCoreRuntimeTests(Database database)
{
    private static readonly Process Fulfillment = Shop.Fulfillment.Processes.OrderFulfillment;

    [Fact]
    public async Task an_order_is_placed_paid_confirmed_and_shipped()
    {
        var shop = Database.Shop(await database.NewAsync());
        await shop.SendAsync(new Restock("KEYBOARD", 5));

        var decision = await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await DrainAsync(shop);

        Assert.IsType<Decision.Accepted>(decision);
        Assert.Equal(ProcessStatus.Awaiting, await shop.StatusOfAsync(Fulfillment, "A-1"));
        Assert.Equal("Paid", (await shop.ViewAsync<OrderSummary>("A-1"))!.Status);

        await shop.PublishAsync(new PaymentConfirmed("A-1"));
        await DrainAsync(shop);

        Assert.Equal(ProcessStatus.Completed, await shop.StatusOfAsync(Fulfillment, "A-1"));
        Assert.Equal("Shipped", (await shop.ViewAsync<OrderSummary>("A-1"))!.Status);
        Assert.Equal(4, (await shop.StateOfAsync<StockItem>("KEYBOARD")).Available);

        // The confirmation ended the wait, so its 15-minute timer was cancelled with it: nothing is left.
        Assert.Equal((0, 0), await shop.BacklogAsync());
    }

    [Fact]
    public async Task an_instance_recorded_on_the_database_replays_locally_with_urd()
    {
        var shop = Database.Shop(await database.NewAsync());
        await shop.SendAsync(new Restock("KEYBOARD", 5));
        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 2, 2_500m)); // declined: compensated
        await DrainAsync(shop);

        // "Production" is the database; the replay runs in memory, against the same code.
        var history = InstanceHistory.FromJson((await shop.HistoryOfAsync(Fulfillment, "A-1"))!.ToJson());
        Assert.Contains(history.Entries, e => e.Kind == HistoryKind.Compensated);

        (await Urd.ReplayAsync(ShopModule.CreateRuntime, Fulfillment, history)).ThrowIfDiverged();
    }

    [Fact]
    public async Task read_models_keep_their_stream_order()
    {
        var shop = Database.Shop(await database.NewAsync());
        await shop.SendAsync(new Restock("KEYBOARD", 5));
        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await shop.PublishAsync(new PaymentConfirmed("A-1"));
        await DrainAsync(shop);

        Assert.Equal("Shipped", (await shop.ViewAsync<OrderSummary>("A-1"))!.Status);
    }

    [Fact]
    public async Task views_are_listed_in_creation_order()
    {
        var shop = Database.Shop(await database.NewAsync());
        await shop.SendAsync(new Restock("KEYBOARD", 5));
        await shop.SendAsync(new PlaceOrder("B-2", "KEYBOARD", 1, 89m));
        await DrainAsync(shop);
        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await DrainAsync(shop);

        Assert.Equal(["B-2", "A-1"], (await shop.ViewsAsync<OrderSummary>()).Select(o => o.OrderId));
    }

    [Fact]
    public async Task only_a_suspended_instance_can_be_resumed()
    {
        var shop = Database.Shop(await database.NewAsync());
        await shop.SendAsync(new Restock("KEYBOARD", 5));
        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await shop.PublishAsync(new PaymentConfirmed("A-1"));
        await DrainAsync(shop);
        Assert.Equal(ProcessStatus.Completed, await shop.StatusOfAsync(Fulfillment, "A-1"));

        await Assert.ThrowsAsync<InstanceStateException>(() => shop.ResumeAsync(Fulfillment, "A-1"));
    }

    [Fact]
    public async Task a_declined_card_compensates_and_cancels_the_order()
    {
        var shop = Database.Shop(await database.NewAsync());
        await shop.SendAsync(new Restock("KEYBOARD", 5));

        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 2, 2_500m));
        await DrainAsync(shop);

        Assert.Equal(ProcessStatus.Compensated, await shop.StatusOfAsync(Fulfillment, "A-1"));
        Assert.Equal("Cancelled", (await shop.ViewAsync<OrderSummary>("A-1"))!.Status);
        Assert.Equal(5, (await shop.StateOfAsync<StockItem>("KEYBOARD")).Available);
    }

    [Fact]
    public async Task a_timeout_fires_from_the_durable_timer()
    {
        var clock = Database.Clock();
        var shop = Database.Shop(await database.NewAsync(), clock);
        await shop.SendAsync(new Restock("KEYBOARD", 5));
        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await DrainAsync(shop);

        clock.Advance(TimeSpan.FromMinutes(14));
        await DrainAsync(shop);
        Assert.Equal(ProcessStatus.Awaiting, await shop.StatusOfAsync(Fulfillment, "A-1"));

        clock.Advance(TimeSpan.FromMinutes(1));
        await DrainAsync(shop);
        Assert.Equal(ProcessStatus.Compensated, await shop.StatusOfAsync(Fulfillment, "A-1"));
        Assert.Equal(5, (await shop.StateOfAsync<StockItem>("KEYBOARD")).Available);
    }

    [Fact]
    public async Task a_process_survives_a_restart()
    {
        var contexts = await database.NewAsync();
        var before = Database.Shop(contexts);
        await before.SendAsync(new Restock("KEYBOARD", 5));
        await before.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await DrainAsync(before);

        // A new process starts on the same database: the order is still waiting where it was.
        var after = Database.Shop(contexts);
        Assert.Equal(ProcessStatus.Awaiting, await after.StatusOfAsync(Fulfillment, "A-1"));

        await after.PublishAsync(new PaymentConfirmed("A-1"));
        await DrainAsync(after);

        Assert.Equal(ProcessStatus.Completed, await after.StatusOfAsync(Fulfillment, "A-1"));
    }

    [Fact]
    public async Task concurrent_workers_keep_the_business_consistent()
    {
        const int orders = 30;
        var shop = Database.Shop(await database.NewAsync());
        await shop.SendAsync(new Restock("KEYBOARD", 100));

        for (var i = 1; i <= orders; i++)
        {
            await shop.SendAsync(new PlaceOrder($"A-{i}", "KEYBOARD", 1, 89m));
            await shop.PublishAsync(new PaymentConfirmed($"A-{i}"));
        }

        // Four workers race over the same queue, the same stock item and the same instances.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => DrainAsync(shop, cancellationToken: timeout.Token))));

        var summaries = await shop.ViewsAsync<OrderSummary>();
        Assert.Equal(orders, summaries.Count(o => o.Status == "Shipped"));
        Assert.Equal(100 - orders, (await shop.StateOfAsync<StockItem>("KEYBOARD")).Available);
        Assert.Equal((0, 0), await shop.BacklogAsync());
    }

    [Fact]
    public async Task workers_projecting_different_streams_into_one_view_lose_no_update()
    {
        const int items = 40;
#pragma warning disable IL2026, IL3050 // reflection JSON: the view is not in the shop's JSON context
        var shop = ShopModule.Register(new EfCoreRuntime<WyrdDbContext>(await database.NewAsync(), Database.Retention, Database.Options))
            .AddProjection(new AllStocks());
#pragma warning restore IL2026, IL3050
        for (var i = 1; i <= items; i++)
        {
            await shop.SendAsync(new Restock($"SKU-{i:00}", 1)); // one stream each, one view for all
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => DrainAsync(shop, cancellationToken: timeout.Token))));

        Assert.Equal(new AllStock(items), await shop.ViewAsync<AllStock>("all"));
    }

    [Fact]
    public async Task a_step_whose_command_nobody_can_handle_suspends_its_instance_without_blocking_the_queue()
    {
        // ShipOrder has no owner here, so the step's command can never be handled: it reaches the process as a
        // failure, the step has no retries, and the instance is suspended with a reason. No dead letter.
        Process broken = Process.StartOn<OrderPlaced>(e => e.OrderId, "Broken").Then(e => new ShipOrder(e.OrderId)).NoRetry("test");
        var runtime = new EfCoreRuntime<WyrdDbContext>(await database.NewAsync(), ShopJson.Default, Database.Retention, Database.Options with { MaxDeliveryAttempts = 3 })
            .AddSlice(new PlaceOrderSlice())
            .AddSlice(new RestockSlice())
            .AddProcess(broken);

        await runtime.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await runtime.SendAsync(new Restock("KEYBOARD", 3)); // unrelated work behind it still flows
        await DrainAsync(runtime, settle: TimeSpan.FromSeconds(1));

        Assert.Equal(ProcessStatus.Suspended, await runtime.StatusOfAsync(broken, "A-1"));
        Assert.Equal((0, 0), await runtime.BacklogAsync());
        Assert.Equal(3, (await runtime.StateOfAsync<StockItem>("KEYBOARD")).Available);
    }

    [Fact]
    public async Task a_process_command_the_host_cant_handle_reaches_its_process_as_a_failure()
    {
#pragma warning disable IL2026, IL3050 // reflection JSON keeps the test domain small
        var runtime = new EfCoreRuntime<WyrdDbContext>(await database.NewAsync(), Database.Retention, Database.Options)
            .AddSlice(new StartJobSlice())
            .AddSlice(new BrokenSlice())
            .AddProcess(Jobs.Process);
#pragma warning restore IL2026, IL3050

        await runtime.SendAsync(new StartJob("J-1"));
        for (var i = 0; i < 20 && await runtime.ProcessPendingAsync() > 0; i++)
        {
        }

        // Three attempts (one + Retry(2.Times)), decided by the process, then suspended with a reason: no dead letter,
        // and no hidden redeliveries multiplying the attempts.
        Assert.Equal(ProcessStatus.Suspended, await runtime.StatusOfAsync(Jobs.Process, "J-1"));
        var history = (await runtime.HistoryOfAsync(Jobs.Process, "J-1"))!;
        Assert.Equal(3, history.Entries.Count(e => e.Kind == HistoryKind.Sent));
        Assert.Equal("JobSteps.Run failed after 3 attempts", history.Entries.Last(e => e.Kind == HistoryKind.Suspended).Reason);
        Assert.Equal((0, 0), await runtime.BacklogAsync());
    }

    [Fact]
    public async Task a_message_no_process_decides_on_ends_in_the_dead_letters_without_blocking_the_queue()
    {
#pragma warning disable IL2026, IL3050 // reflection JSON keeps the test domain small
        var runtime = new EfCoreRuntime<WyrdDbContext>(await database.NewAsync(), Database.Retention, Database.Options with { MaxDeliveryAttempts = 3 })
            .AddSlice(new StartJobSlice())
            .AddProjection(new BrokenJobs());
#pragma warning restore IL2026, IL3050

        await runtime.SendAsync(new StartJob("J-1"));
        Assert.IsType<Decision.Accepted>(await runtime.SendAsync(new StartJob("J-2"))); // work behind it still flows
        await DrainAsync(runtime, settle: TimeSpan.FromSeconds(1));

        Assert.Equal((0, 2), await runtime.BacklogAsync()); // both JobStarted events, after 3 attempts each
    }

    [Fact]
    public async Task workers_racing_over_failing_messages_record_each_failure_once()
    {
#pragma warning disable IL2026, IL3050 // reflection JSON keeps the test domain small
        var runtime = new EfCoreRuntime<WyrdDbContext>(await database.NewAsync(), Database.Retention, Database.Options with { MaxDeliveryAttempts = 3 })
            .AddSlice(new StartJobSlice())
            .AddProjection(new BrokenJobs());
#pragma warning restore IL2026, IL3050

        for (var i = 1; i <= 10; i++)
        {
            await runtime.SendAsync(new StartJob($"J-{i}"));
        }

        // A failed attempt releases its row lock before the failure is written: the others must not record it again.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => DrainAsync(runtime, TimeSpan.FromSeconds(1), timeout.Token))));

        Assert.Equal((0, 10), await runtime.BacklogAsync());
    }

    [Fact]
    public async Task a_tracestate_longer_than_512_characters_crosses_with_its_message()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Wyrd",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        var state = string.Join(',', Enumerable.Range(1, 32).Select(i => $"vendor{i}={new string('x', 20)}"));
        var transport = new RecordingTransport();
        var shop = Database.Shop(await database.NewAsync()).UseTransport(transport);

        using (var request = new Activity("request").SetIdFormat(ActivityIdFormat.W3C))
        {
            request.TraceStateString = state;
            request.Start();
            await ShipAsync(shop, "A-1");
        }

        Assert.Equal(2, await shop.RelayPendingAsync());
        Assert.All(transport.Sent, message => Assert.Equal(state, message.TraceState));
    }

    [Fact]
    public async Task wyrd_commits_together_with_the_applications_own_tables()
    {
        var contexts = await database.NewAsync<AppDbContext>(options => new(options));
        var shop = ShopModule.Register(new EfCoreRuntime<AppDbContext>(contexts, ShopJson.Default, Database.Retention, Database.Options));

        await using (var db = contexts.CreateDbContext())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Notes.Add(new Note { Text = "restocked after the audit" });
            Assert.IsType<Decision.Accepted>(await shop.SendAsync(new Restock("KEYBOARD", 5), db));
            await transaction.CommitAsync();
        }

        await using (var db = contexts.CreateDbContext())
        await using (await db.Database.BeginTransactionAsync())
        {
            db.Notes.Add(new Note { Text = "never committed" });
            await shop.SendAsync(new Restock("KEYBOARD", 100), db);
            // disposed without commit: both changes roll back
        }

        await using var check = contexts.CreateDbContext();
        Assert.Equal(["restocked after the audit"], await check.Notes.Select(n => n.Text).ToListAsync());
        Assert.Equal(5, (await shop.StateOfAsync<StockItem>("KEYBOARD")).Available);
    }

    [Fact]
    public async Task a_conflict_in_your_transaction_is_a_concurrency_conflict_to_retry()
    {
        var contexts = await database.NewAsync<AppDbContext>(options => new(options));
        var shop = ShopModule.Register(new EfCoreRuntime<AppDbContext>(contexts, ShopJson.Default, Database.Retention, Database.Options));
        await using var first = contexts.CreateDbContext();
        await using var second = contexts.CreateDbContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();

        await shop.SendAsync(new Restock("KEYBOARD", 5), first);
        var racing = shop.SendAsync(new Restock("KEYBOARD", 3), second); // waits for the first transaction's key
        await using (var observer = contexts.CreateDbContext())
        {
            await database.UntilBlockedAsync(observer);
        }

        await firstTransaction.CommitAsync();

        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => racing);
        Assert.NotNull(conflict.InnerException);
        Assert.Equal(5, (await shop.StateOfAsync<StockItem>("KEYBOARD")).Available);
    }

    [Fact]
    public async Task sending_into_a_context_needs_its_transaction()
    {
        var contexts = await database.NewAsync<AppDbContext>(options => new(options));
        var shop = ShopModule.Register(new EfCoreRuntime<AppDbContext>(contexts, ShopJson.Default, Database.Retention, Database.Options));
        await using var db = contexts.CreateDbContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => shop.SendAsync(new Restock("KEYBOARD", 5), db));
    }

    [Fact]
    public async Task what_one_service_publishes_reaches_another_through_the_outbox()
    {
        var transport = new InMemoryTransport();
        var shop = Database.Shop(await database.NewAsync()).UseTransport(transport);
        var shipping = Receiver(await database.NewAsync());
        transport.Subscribe(shipping);

        await ShipAsync(shop, "A-1");
        var relayed = await shop.RelayPendingAsync();
        await DrainAsync(shipping);

        Assert.Equal(2, relayed); // PaymentChargedV1, OrderShippedV1
        Assert.Equal(new Shipment("A-1", 1), await shipping.ViewAsync<Shipment>("A-1"));
        Assert.Equal(0, await shop.RelayPendingAsync());
    }

    [Fact]
    public async Task a_message_delivered_twice_is_handled_once()
    {
        var transport = new RecordingTransport();
        var shop = Database.Shop(await database.NewAsync()).UseTransport(transport);
        var shipping = Receiver(await database.NewAsync());
        await ShipAsync(shop, "A-1");
        await shop.RelayPendingAsync();

        foreach (var message in transport.Sent.Concat(transport.Sent))
        {
            await shipping.ReceiveAsync(message);
        }

        await DrainAsync(shipping);
        Assert.Equal(new Shipment("A-1", 1), await shipping.ViewAsync<Shipment>("A-1"));
    }

    [Fact]
    public async Task while_the_transport_is_down_published_messages_wait_in_the_outbox()
    {
        var transport = new RecordingTransport { Down = true };
        var shop = Database.Shop(await database.NewAsync()).UseTransport(transport);
        await ShipAsync(shop, "A-1");

        await Assert.ThrowsAsync<IOException>(() => shop.RelayPendingAsync());
        transport.Down = false;

        Assert.Equal(2, await shop.RelayPendingAsync());
        Assert.Equal(2, transport.Sent.Select(m => m.MessageId).Distinct().Count());
    }

    [Fact]
    public async Task relays_in_several_workers_send_each_message_once()
    {
        var transport = new RecordingTransport();
        var contexts = await database.NewAsync();
        var shop = Database.Shop(contexts);
        foreach (var order in Enumerable.Range(1, 10))
        {
            await ShipAsync(shop, $"A-{order}");
        }

        var small = Database.Options with { RelayBatchSize = 3 };
        var relays = Enumerable.Range(0, 4).Select(_ =>
            ShopModule.Register(new EfCoreRuntime<WyrdDbContext>(contexts, ShopJson.Default, Database.Retention, small)).UseTransport(transport).RelayPendingAsync());
        var relayed = await Task.WhenAll(relays);

        Assert.Equal(20, relayed.Sum());
        Assert.Equal(20, transport.Sent.Select(m => m.MessageId).Distinct().Count());
        Assert.Equal(20, transport.Sent.Count);
    }

    [Fact]
    public async Task a_contract_nobody_uses_here_is_ignored()
    {
        var shipping = Receiver(await database.NewAsync());
        var message = new TransportMessage(Guid.CreateVersion7(), "payments.refund-issued v1", "{}");

        await shipping.ReceiveAsync(message);

        Assert.Equal((0, 0), await shipping.BacklogAsync());
    }

    private static EfCoreRuntime<WyrdDbContext> Receiver(IDbContextFactory<WyrdDbContext> contexts) =>
        new EfCoreRuntime<WyrdDbContext>(contexts, Database.Retention, Database.Options).AddProjection(new Shipments());

    /// <summary>Places, pays and ships an order (the shop publishes PaymentChargedV1 and OrderShippedV1).</summary>
    private static async Task ShipAsync(EfCoreRuntime<WyrdDbContext> shop, string orderId)
    {
        await shop.SendAsync(new Restock("KEYBOARD", 1));
        await shop.SendAsync(new PlaceOrder(orderId, "KEYBOARD", 1, 89m));
        await DrainAsync(shop);
        await shop.PublishAsync(new PaymentConfirmed(orderId));
        await DrainAsync(shop);
    }

    [Fact]
    public async Task the_queue_health_shows_lag_pending_steps_and_suspended_instances()
    {
        var clock = Database.Clock();
#pragma warning disable IL2026, IL3050 // reflection JSON keeps the test domain small
        using var runtime = new EfCoreRuntime<WyrdDbContext>(await database.NewAsync(), Database.Retention, Database.Options, clock)
            .AddSlice(new StartJobSlice())
            .AddSlice(new BrokenSlice())
            .AddProcess(Jobs.Process);
#pragma warning restore IL2026, IL3050

        await runtime.SendAsync(new StartJob("J-1"));
        Assert.Equal(new QueueHealth(1, TimeSpan.Zero, 0, 0, 0, TimeSpan.Zero, 0, 0), await runtime.HealthAsync());

        await runtime.ProcessPendingAsync(max: 1); // the process starts and sends RunJob
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(new QueueHealth(1, TimeSpan.FromSeconds(30), 0, 0, 1, TimeSpan.FromSeconds(30), 0, 0), await runtime.HealthAsync());

        while (await runtime.ProcessPendingAsync() > 0)
        {
            clock.Advance(TimeSpan.FromMinutes(1)); // redeliveries become due
        }

        // RunJob always fails: after its retries the instance needs an operator, and nothing is pending any more.
        var health = await runtime.HealthAsync();
        Assert.Equal((0, 1L, 0L), (health.PendingSteps, health.Suspended, health.Due));
    }

    [Fact]
    public async Task a_message_queued_by_this_runtime_wakes_its_idle_worker()
    {
        // Workers that would otherwise look again only after ten seconds.
        var slow = Database.Options with { PollInterval = TimeSpan.FromSeconds(10), MaxPollInterval = TimeSpan.FromSeconds(10) };
        using var shop = ShopModule.Register(new EfCoreRuntime<WyrdDbContext>(await database.NewAsync(), ShopJson.Default, Database.Retention, slow));
        using var stop = new CancellationTokenSource();
        var worker = shop.RunAsync(stop.Token);
        try
        {
            var started = Stopwatch.StartNew();
            while (shop.IdleWaits == 0) // idle, waiting: the message is sent after the worker's first look
            {
                Assert.True(started.Elapsed < TimeSpan.FromSeconds(10), "the worker never went idle");
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            var sent = Stopwatch.StartNew();
            await shop.SendAsync(new Restock("KEYBOARD", 5));
            while ((await shop.BacklogAsync()).Queued > 0)
            {
                Assert.True(sent.Elapsed < TimeSpan.FromSeconds(5), "the worker was not woken");
                await Task.Delay(TimeSpan.FromMilliseconds(20));
            }
        }
        finally
        {
            await stop.CancelAsync();
            await worker;
        }
    }

    [Fact]
    public async Task a_runtime_disposed_after_a_commit_doesnt_fail_the_caller()
    {
        var shop = Database.Shop(await database.NewAsync());
        shop.Dispose();

        Assert.IsType<Decision.Accepted>(await shop.SendAsync(new Restock("KEYBOARD", 5)));
    }

    [Fact]
    public async Task a_max_poll_interval_shorter_than_the_poll_interval_is_rejected()
    {
        var contexts = await database.NewAsync();
        var options = Database.Options with { PollInterval = TimeSpan.FromSeconds(5), MaxPollInterval = TimeSpan.FromSeconds(1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new EfCoreRuntime<WyrdDbContext>(contexts, ShopJson.Default, Database.Retention, options));
    }

    [Fact]
    public async Task an_expired_instance_is_archived_before_it_is_removed_and_still_replays()
    {
        var clock = Database.Clock();
        var archive = new FlakyArchive();
        var shop = Database.Shop(await database.NewAsync(), clock, Database.Retention with { FinishedInstances = Keep.For(TimeSpan.FromDays(30)) })
            .UseHistoryArchive(archive);
        await ShipAsync(shop, "A-1");
        clock.Advance(TimeSpan.FromDays(30));

        await shop.ForgetExpiredAsync(); // the archive is down: nothing is removed, and the health says so
        Assert.Equal(ProcessStatus.Completed, await shop.StatusOfAsync(Fulfillment, "A-1"));
        Assert.Equal(1, (await shop.HealthAsync()).ExpiredKept);

        await shop.ForgetExpiredAsync();
        Assert.Null(await shop.StatusOfAsync(Fulfillment, "A-1"));
        var archived = Assert.Single(archive.Histories);
        (await Urd.ReplayAsync(ShopModule.CreateRuntime, Fulfillment, InstanceHistory.FromJson(archived.ToJson()))).ThrowIfDiverged();
    }

    private sealed class FlakyArchive : IHistoryArchive
    {
        private bool _down = true;

        public List<InstanceHistory> Histories { get; } = [];

        public Task ArchiveAsync(IReadOnlyList<InstanceHistory> histories, CancellationToken cancellationToken)
        {
            if (_down)
            {
                _down = false;
                throw new IOException("storage unavailable");
            }

            Histories.AddRange(histories);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task retention_forgets_what_expired_and_keeps_the_rest()
    {
        var clock = Database.Clock();
        var contexts = await database.NewAsync();
        var shop = Database.Shop(contexts, clock, new Retention
        {
            Deduplication = Keep.For(TimeSpan.FromDays(7)),
            FinishedInstances = Keep.For(TimeSpan.FromDays(30)),
            RelayedMessages = Keep.For(TimeSpan.FromDays(7)),
            Events = Keep.Forever("the record of what happened"),
            DeadLetters = Keep.For(TimeSpan.FromDays(7)),
        });
        await ShipAsync(shop, "A-1");
        var before = await CountAsync(contexts);
        Assert.All([before.Inbox, before.Handled, before.Published, before.Events], count => Assert.NotEqual(0, count));

        clock.Advance(TimeSpan.FromDays(7));
        await shop.ForgetExpiredAsync(batchSize: 2); // small batches: several rounds per table
        var week = await CountAsync(contexts);
        Assert.Equal((0, 0, 0), (week.Inbox, week.Handled, week.Published));
        Assert.Equal(ProcessStatus.Completed, await shop.StatusOfAsync(Fulfillment, "A-1"));

        clock.Advance(TimeSpan.FromDays(23));
        await shop.ForgetExpiredAsync();
        Assert.Null(await shop.StatusOfAsync(Fulfillment, "A-1"));
        Assert.Equal(before.Events, (await CountAsync(contexts)).Events); // kept forever, on purpose
        Assert.Equal(1, (await shop.ViewAsync<OrderSummary>("A-1")) is null ? 0 : 1); // read models are the application's
    }

    [Fact]
    public async Task a_suspended_instance_never_expires()
    {
        var clock = Database.Clock();
        Process broken = Process.StartOn<OrderPlaced>(e => e.OrderId, "Broken").Then(e => new ShipOrder(e.OrderId)).NoRetry("test");
        var expiring = new Retention
        {
            Deduplication = Keep.For(TimeSpan.FromDays(1)),
            FinishedInstances = Keep.For(TimeSpan.FromDays(1)),
            RelayedMessages = Keep.For(TimeSpan.FromDays(1)),
            Events = Keep.For(TimeSpan.FromDays(1)),
            DeadLetters = Keep.For(TimeSpan.FromDays(1)),
        };
        var runtime = new EfCoreRuntime<WyrdDbContext>(await database.NewAsync(), ShopJson.Default, expiring, Database.Options, clock)
            .AddSlice(new PlaceOrderSlice())
            .AddProcess(broken);
        await runtime.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await DrainAsync(runtime, settle: TimeSpan.FromSeconds(1));
        Assert.Equal(ProcessStatus.Suspended, await runtime.StatusOfAsync(broken, "A-1"));

        clock.Advance(TimeSpan.FromDays(365));
        await runtime.ForgetExpiredAsync();

        Assert.Equal(ProcessStatus.Suspended, await runtime.StatusOfAsync(broken, "A-1"));
    }

    [Fact]
    public async Task a_fence_outlives_the_deduplication_window()
    {
        var clock = Database.Clock();
        var contexts = await database.NewAsync();
        var shop = Database.Shop(contexts, clock, Database.Retention with { Deduplication = Keep.For(TimeSpan.FromDays(7)) });
        Guid fenced = Guid.NewGuid(), answered = Guid.NewGuid();
        await using (var db = await contexts.CreateDbContextAsync())
        {
            var store = new EfCoreStore(db, clock.GetUtcNow());
            var changes = new Changes();
            changes.Replies[fenced] = new ReplyMessage(ReplyOutcome.Fenced);
            changes.Replies[answered] = new ReplyMessage(ReplyOutcome.Rejected);
            await store.CommitAsync(changes, CancellationToken.None);
        }

        clock.Advance(TimeSpan.FromDays(365));
        await shop.ForgetExpiredAsync();

        // A late copy of a fenced command must never run, however late it is (semantics 3.4).
        await using var check = await contexts.CreateDbContextAsync();
        Assert.Equal([fenced], await check.Set<InboxRow>().Select(r => r.MessageId).ToListAsync());
    }

    private static async Task<(int Inbox, int Handled, int Published, int Events)> CountAsync(IDbContextFactory<WyrdDbContext> contexts)
    {
        await using var db = await contexts.CreateDbContextAsync();
        return (await db.Set<InboxRow>().CountAsync(), await db.Set<HandledRow>().CountAsync(),
            await db.Set<PublishedRow>().CountAsync(), await db.Set<EventRow>().CountAsync());
    }

    /// <summary>Processes until the queue has nothing due, waiting a little for short redelivery delays.</summary>
    private static async Task DrainAsync<TContext>(EfCoreRuntime<TContext> runtime, TimeSpan? settle = null, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        var quietUntil = DateTime.UtcNow + (settle ?? TimeSpan.FromMilliseconds(200));
        while (DateTime.UtcNow < quietUntil && !cancellationToken.IsCancellationRequested)
        {
            if (await runtime.ProcessPendingAsync(cancellationToken: cancellationToken) > 0)
            {
                quietUntil = DateTime.UtcNow + (settle ?? TimeSpan.FromMilliseconds(200));
            }
            else
            {
                await Task.Delay(20, cancellationToken);
            }
        }
    }
}
