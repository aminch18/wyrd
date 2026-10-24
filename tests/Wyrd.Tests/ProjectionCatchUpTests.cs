using System.Diagnostics.CodeAnalysis;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class ProjectionCatchUpTests
{
    public sealed record OrderLog(string OrderId, int Events);

    /// <summary>Counts the events of each order (adding, so an event applied twice shows): a seed takes the count from the state.</summary>
    private sealed class OrderLogs : Projection<OrderLog>
    {
        public OrderLogs()
        {
            Seed<EventLog>((orderId, _) => orderId, (_, orderId, log) => new OrderLog(orderId, log.Events.Count));
            On<OrderPlaced>(e => e.OrderId, (view, e) => new OrderLog(e.OrderId, (view?.Events ?? 0) + 1));
            On<PaymentCharged>(e => e.OrderId, (view, _) => view! with { Events = view.Events + 1 });
        }
    }

    [Fact]
    public async Task a_projection_added_to_a_runtime_with_states_catches_up_from_them()
    {
        var runtime = TestSystem.Create();
        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.SendAsync(new ChargePayment("A-1", 50));
        await runtime.SendAsync(new PlaceOrder("A-2", Quantity: 1, Amount: 70));

        runtime.AddProjection(new OrderLogs());
        await runtime.CatchUpAsync();

        Assert.Equal(new OrderLog("A-1", 2), runtime.View<OrderLog>("A-1"));
        Assert.Equal(new OrderLog("A-2", 1), runtime.View<OrderLog>("A-2"));
        Assert.Contains(runtime.Journal, e => e.Message == "OrderLog caught up from existing states");

        await runtime.SendAsync(new ChargePayment("A-2", 70));
        Assert.Equal(new OrderLog("A-2", 2), runtime.View<OrderLog>("A-2"));
    }

    [Fact]
    public async Task a_projection_with_seeds_on_a_new_runtime_follows_every_stream_from_its_first_event()
    {
        var runtime = TestSystem.Create().AddProjection(new OrderLogs());

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.SendAsync(new ChargePayment("A-1", 50));

        Assert.Equal(new OrderLog("A-1", 2), runtime.View<OrderLog>("A-1"));
    }

    [Fact]
    [RequiresUnreferencedCode("Reflection-based JSON in a test.")]
    [RequiresDynamicCode("Reflection-based JSON in a test.")]
    public async Task events_already_in_a_seed_are_not_applied_again_when_they_arrive()
    {
        var application = new Application(MessageTypes.Reflection());
        application.Add(new PlaceOrderSlice());
        application.Add(new ChargePaymentSlice());
        var store = new InMemoryStore(() => DateTimeOffset.UnixEpoch);
        var ids = 0;
        var engine = new Engine(
            application.Registry, store, application.Types, () => DateTimeOffset.UnixEpoch, () => new Guid(++ids, 0, 0, new byte[8]), (_, _) => false, (_, _, _) => { });

        await engine.HandleAsync(Command(application, new PlaceOrder("A-1", Quantity: 1, Amount: 50)), default);
        await engine.HandleAsync(Command(application, new ChargePayment("A-1", 50)), default);
        var inFlight = await store.TakeOutboxAsync(default); // OrderPlaced and PaymentCharged, not delivered yet

        var projection = new OrderLogs();
        application.Add(projection);
        await engine.SeedAsync(projection, store.State(typeof(EventLog).FullName!, "A-1")!, default);
        Assert.Equal(new OrderLog("A-1", 2), View(application, store, "A-1"));
        Assert.False(await engine.CompleteCatchUpAsync(projection, default)); // what the seed holds is still on its way

        foreach (var envelope in inFlight)
        {
            await engine.HandleAsync(envelope, default);
        }

        Assert.Equal(new OrderLog("A-1", 2), View(application, store, "A-1"));
        Assert.True(await engine.CompleteCatchUpAsync(projection, default));

        await engine.HandleAsync(Command(application, new ChargePayment("A-1", 50)), default);
        foreach (var envelope in await store.TakeOutboxAsync(default))
        {
            await engine.HandleAsync(envelope, default);
        }

        Assert.Equal(new OrderLog("A-1", 3), View(application, store, "A-1"));
    }

    [Fact]
    [RequiresUnreferencedCode("Reflection-based JSON in a test.")]
    [RequiresDynamicCode("Reflection-based JSON in a test.")]
    public async Task a_seed_built_from_a_state_an_event_changed_meanwhile_is_not_committed()
    {
        var application = new Application(MessageTypes.Reflection());
        application.Add(new PlaceOrderSlice());
        application.Add(new ChargePaymentSlice());
        var store = new InMemoryStore(() => DateTimeOffset.UnixEpoch);
        var ids = 0;
        var engine = new Engine(
            application.Registry, store, application.Types, () => DateTimeOffset.UnixEpoch, () => new Guid(++ids, 0, 0, new byte[8]), (_, _) => false, (_, _, _) => { });

        await engine.HandleAsync(Command(application, new PlaceOrder("A-1", Quantity: 1, Amount: 50)), default);
        var read = store.State(typeof(EventLog).FullName!, "A-1")!;
        await engine.HandleAsync(Command(application, new ChargePayment("A-1", 50)), default);

        var projection = new OrderLogs();
        application.Add(projection);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () => await engine.SeedAsync(projection, read, default));
        Assert.Null(View(application, store, "A-1"));
    }

    private static Envelope Command(Application application, ICommand command)
    {
        var (type, payload) = application.Types.Serialize(command, command.GetType());
        return new Envelope(Guid.NewGuid(), MessageKind.Command, type, payload);
    }

    private static OrderLog? View(Application application, InMemoryStore store, string key) =>
        store.View(typeof(OrderLog).FullName!, key) is { } view ? (OrderLog)application.Types.Deserialize(view.Type!, view.Payload!) : null;
}
