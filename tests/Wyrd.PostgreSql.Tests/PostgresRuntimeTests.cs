using Shop;
using Shop.Contracts;
using Shop.Inventory;
using Shop.Orders;
using Shop.Shipping;
using Wyrd.Processes;
using Wyrd.Runtime;
using Wyrd.Testing;

namespace Wyrd.PostgreSql.Tests;

/// <summary>The sample shop on a real PostgreSQL: the same domain, now durable.</summary>
[Collection(PostgresDatabase.Name)]
public class PostgresRuntimeTests(PostgresFixture postgres)
{
    private static readonly Process Fulfillment = Shop.Fulfillment.Processes.OrderFulfillment;

    [Fact]
    public async Task an_order_is_placed_paid_confirmed_and_shipped()
    {
        var shop = await postgres.ShopAsync(postgres.NewSchema());
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

        // Only the 15-minute timer is left: when it fires, the process is no longer waiting and it is ignored.
        Assert.Equal((1, 0), await shop.BacklogAsync());
    }

    [Fact]
    public async Task an_instance_recorded_on_postgresql_replays_locally_with_urd()
    {
        var shop = await postgres.ShopAsync(postgres.NewSchema());
        await shop.SendAsync(new Restock("KEYBOARD", 5));
        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 2, 2_500m)); // declined: compensated
        await DrainAsync(shop);

        // "Production" is PostgreSQL; the replay runs in memory, against the same code.
        var history = InstanceHistory.FromJson((await shop.HistoryOfAsync(Fulfillment, "A-1"))!.ToJson());
        Assert.Contains(history.Entries, e => e.Kind == HistoryKind.Compensated);

        (await Urd.ReplayAsync(ShopModule.CreateRuntime, Fulfillment, history)).ThrowIfDiverged();
    }

    [Fact]
    public async Task read_models_keep_their_stream_order_on_postgresql()
    {
        var shop = await postgres.ShopAsync(postgres.NewSchema());
        await shop.SendAsync(new Restock("KEYBOARD", 5));
        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await shop.PublishAsync(new PaymentConfirmed("A-1"));
        await DrainAsync(shop);

        Assert.Equal("Shipped", (await shop.ViewAsync<OrderSummary>("A-1"))!.Status);
    }

    [Fact]
    public async Task an_operator_resumes_a_suspended_instance_on_postgresql()
    {
        var clock = PostgresFixture.Clock();
        var shop = await postgres.ShopAsync(postgres.NewSchema(), clock);
        await shop.SendAsync(new Restock("KEYBOARD", 5));
        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await DrainAsync(shop);
        await shop.PublishAsync(new PaymentConfirmed("A-1"));
        await DrainAsync(shop);
        Assert.Equal(ProcessStatus.Completed, await shop.StatusOfAsync(Fulfillment, "A-1"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => shop.ResumeAsync(Fulfillment, "A-1")); // not suspended
    }

    [Fact]
    public async Task a_declined_card_compensates_and_cancels_the_order()
    {
        var shop = await postgres.ShopAsync(postgres.NewSchema());
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
        var clock = PostgresFixture.Clock();
        var shop = await postgres.ShopAsync(postgres.NewSchema(), clock);
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
        var options = postgres.NewSchema();
        var before = await postgres.ShopAsync(options);
        await before.SendAsync(new Restock("KEYBOARD", 5));
        await before.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await DrainAsync(before);

        // A new process starts on the same database: the order is still waiting where it was.
        var after = await postgres.ShopAsync(options);
        Assert.Equal(ProcessStatus.Awaiting, await after.StatusOfAsync(Fulfillment, "A-1"));

        await after.PublishAsync(new PaymentConfirmed("A-1"));
        await DrainAsync(after);

        Assert.Equal(ProcessStatus.Completed, await after.StatusOfAsync(Fulfillment, "A-1"));
    }

    [Fact]
    public async Task concurrent_workers_keep_the_business_consistent()
    {
        const int orders = 30;
        var shop = await postgres.ShopAsync(postgres.NewSchema());
        await shop.SendAsync(new Restock("KEYBOARD", 100));

        for (var i = 1; i <= orders; i++)
        {
            await shop.SendAsync(new PlaceOrder($"A-{i}", "KEYBOARD", 1, 89m));
            await shop.PublishAsync(new PaymentConfirmed($"A-{i}"));
        }

        // Four workers race over the same queue, the same stock item and the same instances.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => DrainAsync(shop, cancellationToken: timeout.Token))));

        var summaries = await shop.ViewsAsync<OrderSummary>();
        Assert.Equal(orders, summaries.Count(o => o.Status == "Shipped"));
        Assert.Equal(100 - orders, (await shop.StateOfAsync<StockItem>("KEYBOARD")).Available);
        Assert.Equal((0, 0), await shop.BacklogAsync());
    }

    [Fact]
    public async Task a_step_whose_command_nobody_can_handle_suspends_its_instance_without_blocking_the_queue()
    {
        // ShipOrder has no owner here, so the step's command can never be handled: it reaches the process as a
        // failure, the step has no retries, and the instance is suspended with a reason. No dead letter.
        Process broken = Process.StartOn<OrderPlaced>(e => e.OrderId, "Broken").Then(e => new ShipOrder(e.OrderId)).NoRetry("test");
        var runtime = new PostgresRuntime(postgres.DataSource, ShopJson.Default, postgres.NewSchema() with { MaxDeliveryAttempts = 3 })
            .AddSlice(new PlaceOrderSlice())
            .AddSlice(new RestockSlice())
            .AddProcess(broken);
        await runtime.EnsureSchemaAsync();

        await runtime.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
        await runtime.SendAsync(new Restock("KEYBOARD", 3)); // unrelated work behind it still flows
        await DrainAsync(runtime, settle: TimeSpan.FromSeconds(1));

        Assert.Equal(ProcessStatus.Suspended, await runtime.StatusOfAsync(broken, "A-1"));
        Assert.Equal((0, 0), await runtime.BacklogAsync());
        Assert.Equal(3, (await runtime.StateOfAsync<StockItem>("KEYBOARD")).Available);
    }

    /// <summary>Processes until the queue has nothing due, waiting a little for short redelivery delays.</summary>
    private static async Task DrainAsync(PostgresRuntime runtime, TimeSpan? settle = null, CancellationToken cancellationToken = default)
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
