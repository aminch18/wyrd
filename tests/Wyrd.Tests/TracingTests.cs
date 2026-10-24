using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Shop;
using Wyrd.Processes;
using Wyrd.Runtime;
using Wyrd.Testing;
using Process = Wyrd.Processes.Process;
using ShopProcesses = Shop.Fulfillment.Processes;

namespace Wyrd.Tests;

[CollectionDefinition("Tracing", DisableParallelization = true)]
public sealed class TracingTestsGroup;

[Collection("Tracing")]
public class TracingTests
{
    private static readonly string[] OrderIds = ["A-1", "A-2", "A-3"];
    [Fact]
    public async Task tracing_does_not_change_the_shop_journal_ids_domain_state_or_replay_history()
    {
        var without = await ShopScenario();
        using var listener = new Recording();
        var with = await ShopScenario();

        Assert.Equal(without.Journal, with.Journal);
        Assert.Equal(without.State, with.State);
        Assert.Equal(without.Histories, with.Histories);
        Assert.Equal(without.Messages, with.Messages);
        Assert.NotEmpty(listener.Activities);
    }

    [Fact]
    public async Task one_consumer_per_envelope_keeps_the_trigger_commands_and_later_webhook_in_one_trace()
    {
        using var listener = new Recording();
        using var runtime = ShopModule.CreateRuntime();
        var probe = new Probe();
        runtime.Simulation = probe;
        await runtime.SendAsync(new Shop.Inventory.Restock("KEYBOARD", 5));
        await runtime.SendAsync(new Shop.Orders.PlaceOrder("trace-happy", "KEYBOARD", 1, 50m));
        var first = listener.Activities.Single(a => Tag(a, "wyrd.message.type") == "Shop.Orders.PlaceOrder");
        using var webhook = new Activity("external webhook").Start();
        await runtime.PublishAsync(new Shop.Contracts.PaymentConfirmed("trace-happy"));

        var activities = listener.Activities.ToArray();
        Assert.Equal(probe.Messages.Count + 2, activities.Length); // two external commands bypass the queue
        Assert.All(activities, a => Assert.Equal(ActivityKind.Consumer, a.Kind));
        Assert.All(activities.Where(a => Tag(a, "wyrd.process.key") == "trace-happy"), a => Assert.Equal(first.TraceId, a.TraceId));
        var incoming = activities.Single(a => Tag(a, "wyrd.message.type") == "psp.payment-confirmed v1");
        Assert.Equal(first.TraceId, incoming.TraceId);
        Assert.Contains(incoming.Links, link => link.Context.TraceId == webhook.TraceId);
        Assert.Contains(activities, a => Tag(a, "wyrd.process.status") == "Completed");
        Assert.Contains(activities, a => a.Events.Any(e => e.Name == "wyrd.waiting"));
        Assert.All(probe.Messages, envelope => Assert.NotNull(envelope.Trace));
        Assert.Same(webhook, Activity.Current);
    }

    [Fact]
    public async Task retries_timeouts_undos_and_outcome_remain_in_the_instance_trace()
    {
        using var listener = new Recording();
        using var runtime = ShopModule.CreateRuntime();
        runtime.FailNext<Shop.Payments.ChargePayment>();
        await runtime.SendAsync(new Shop.Inventory.Restock("KEYBOARD", 1));
        await runtime.SendAsync(new Shop.Orders.PlaceOrder("trace-timeout", "KEYBOARD", 1, 50m));
        await runtime.AdvanceAsync(1.Seconds);
        await runtime.AdvanceAsync(15.Minutes);

        var activities = listener.Activities.Where(a => Tag(a, "wyrd.process.key") == "trace-timeout").ToArray();
        Assert.Single(activities.Select(a => a.TraceId).Distinct());
        Assert.Contains(activities, a => Tag(a, "wyrd.outcome") == "Faulted");
        Assert.Contains(activities, a => Tag(a, "wyrd.attempt") == "2");
        Assert.Contains(activities, a => Tag(a, "wyrd.outcome") == "timeout");
        Assert.Contains(activities, a => Tag(a, "wyrd.message.type") == "Shop.Payments.RefundPayment");
        Assert.Contains(activities, a => Tag(a, "wyrd.message.type") == "Shop.Orders.CancelOrder");
        Assert.Contains(activities, a => Tag(a, "wyrd.process.status") == "Compensated");
        Assert.Contains(activities, a => a.Events.Any(e => e.Name == "wyrd.retry"));
        Assert.Contains(activities, a => Tag(a, "wyrd.compensating") == "True");
    }

    [Fact]
    public async Task duplicates_stale_replies_and_fenced_undos_have_explicit_outcomes()
    {
        using var listener = new Recording();
        using var runtime = TestSystem.Create(FluentProcess.OrderFulfillment);
        runtime.Simulation = new Probe(duplicate: true);
        await runtime.SendAsync(new PlaceOrder("trace-duplicate", 1, 50));
        await runtime.PublishAsync(new PaymentConfirmed("trace-duplicate"));
        Assert.Contains(listener.Activities, a => Tag(a, "wyrd.outcome") == "duplicate");
        Assert.Contains(listener.Activities, a => Tag(a, "wyrd.outcome") == "stale");

        Process uncertain = Process.StartOn<OrderPlaced>(e => e.OrderId, "Uncertain")
            .Then(Stock.Reserve).Retry(1.Times).Undo(Stock.Release)
            .Then(Payment.Charge).NoRetry("Compensate the uncertain result.").OrCompensate().Undo(Payment.Refund);
        using var faulted = TestSystem.Create(uncertain);
        faulted.Simulation = new FencingProbe();
        faulted.FailNext<ChargePayment>();
        await faulted.SendAsync(new PlaceOrder("trace-fenced", 1, 50));
        Assert.Contains(listener.Activities, a => Tag(a, "wyrd.outcome") == "NothingToUndo");
        Assert.Contains(listener.Activities, a => Tag(a, "wyrd.outcome") == "fenced");
    }

    [Fact]
    public async Task a_resumed_instance_keeps_its_original_trace_under_a_new_operator_context()
    {
        using var listener = new Recording();
        using var runtime = ShopModule.CreateRuntime();
        runtime.FailNext<Shop.Payments.ChargePayment>(6);
        await runtime.SendAsync(new Shop.Inventory.Restock("KEYBOARD", 1));
        await runtime.SendAsync(new Shop.Orders.PlaceOrder("trace-resume", "KEYBOARD", 1, 50m));
        await runtime.AdvanceAsync(1.Minutes);
        Assert.Equal(ProcessStatus.Suspended, runtime.StatusOf(ShopProcesses.OrderFulfillment, "trace-resume"));
        var trace = listener.Activities.First(a => Tag(a, "wyrd.process.key") == "trace-resume").TraceId;
        using var operation = new Activity("operator resume").Start();
        await runtime.ResumeAsync(ShopProcesses.OrderFulfillment, "trace-resume");
        await runtime.PublishAsync(new Shop.Contracts.PaymentConfirmed("trace-resume"));
        Assert.Equal(ProcessStatus.Completed, runtime.StatusOf(ShopProcesses.OrderFulfillment, "trace-resume"));
        Assert.All(listener.Activities.Where(a => Tag(a, "wyrd.process.key") == "trace-resume"), a => Assert.Equal(trace, a.TraceId));
    }

    [Fact]
    public async Task one_event_can_resume_two_traces_without_reparenting_or_merging_their_commands()
    {
        using var listener = new Recording();
        Process first = Process.StartOn<OrderPlaced>(e => e.OrderId, "First")
            .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
            .Then(Shipping.Ship).Retry(1.Times);
        Process second = Process.StartOn<StockReserved>(e => e.OrderId, "Second")
            .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
            .Then(ShipReserved).Retry(1.Times);
        using var runtime = TestSystem.Create(first).AddProcess(second);
        await runtime.SendAsync(new PlaceOrder("shared", 1, 50));
        await runtime.SendAsync(new ReserveStock("shared", 1));
        var firstTrace = listener.Activities.Single(a => Tag(a, "wyrd.process.name") == "First").TraceId;
        var secondTrace = listener.Activities.Single(a => Tag(a, "wyrd.process.name") == "Second").TraceId;
        Assert.NotEqual(firstTrace, secondTrace);
        await runtime.PublishAsync(new PaymentConfirmed("shared"));
        var received = listener.Activities.Single(a => Tag(a, "wyrd.message.type") == MessageTypes.NameOf(typeof(PaymentConfirmed)));
        Assert.Equal(firstTrace, received.TraceId);
        Assert.Contains(received.Links, link => link.Context.TraceId == secondTrace);
        var commands = listener.Activities.Where(a => Tag(a, "wyrd.message.kind") == "Command" && Tag(a, "wyrd.process.name") is not null).ToArray();
        Assert.Contains(commands, a => Tag(a, "wyrd.process.name") == "First" && a.TraceId == firstTrace);
        Assert.Contains(commands, a => Tag(a, "wyrd.process.name") == "Second" && a.TraceId == secondTrace);
    }

    private static ShipOrder ShipReserved(StockReserved reserved) => new(reserved.OrderId);

    [Fact]
    public void replay_replies_preserve_command_context_despite_an_unrelated_ambient_activity()
    {
        using var listener = new Recording();
        var trace = new TraceContext("00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
        var command = new Envelope(Guid.Empty, MessageKind.Command, "command", "{}", Trace: trace);
        using var ambient = new Activity("unrelated replay caller").Start();
        var engine = new Engine(new Registry(), new InMemoryStore(() => DateTimeOffset.UnixEpoch), MessageTypes.Reflection(),
            () => DateTimeOffset.UnixEpoch, () => Guid.Empty, (_, _) => false, (_, _, _) => { });
        var reply = engine.ReplyTo(command, new ReplyMessage(ReplyOutcome.Accepted));
        Assert.Equal(trace, reply.Trace);
    }

    [Fact]
    public async Task skuld_keeps_settling_the_shop_with_a_listener_enabled()
    {
        using var listener = new Recording();
        static async Task Afternoon(InMemoryRuntime runtime)
        {
            await runtime.SendAsync(new Shop.Inventory.Restock("KEYBOARD", 2));
            await runtime.SendAsync(new Shop.Orders.PlaceOrder("traced-skuld", "KEYBOARD", 1, 50m));
            await runtime.PublishAsync(new Shop.Contracts.PaymentConfirmed("traced-skuld"));
        }

        var report = await Skuld.SimulateAsync(ShopModule.CreateRuntime, Afternoon,
            [Invariant.ProcessesSettle], new SimulationOptions { Runs = 50, FaultRate = .2, DuplicateRate = .2, ReorderRate = .5 });
        report.ThrowIfViolated();
        Assert.NotEmpty(listener.Activities);
    }

    [Fact]
    public async Task an_unexpected_handler_exception_is_an_error_span_without_changing_the_exception()
    {
        using var listener = new Recording();
        var engine = new Engine(new Registry(), new InMemoryStore(() => DateTimeOffset.UnixEpoch), MessageTypes.Reflection(),
            () => DateTimeOffset.UnixEpoch, () => Guid.Empty, (_, _) => false, (_, _, _) => { });
        await Assert.ThrowsAsync<UnknownMessageTypeException>(async () =>
            await engine.HandleAsync(new Envelope(Guid.Empty, MessageKind.Command, "unknown", "{}"), default));
        var activity = Assert.Single(listener.Activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("exception", Tag(activity, "wyrd.outcome"));
    }

    [Fact]
    public void trace_context_survives_source_generated_envelope_and_instance_json()
    {
        var trace = new TraceContext("00-0123456789abcdef0123456789abcdef-0123456789abcdef-01", "vendor=value");
        var envelope = new Envelope(Guid.Empty, MessageKind.Command, "command", "{}", Trace: trace);
        var json = JsonSerializer.Serialize(envelope, WyrdJsonContext.Default.Envelope);
        Assert.DoesNotContain("Context", json, StringComparison.Ordinal);
        Assert.Contains("\"Parent\"", json, StringComparison.Ordinal);
        Assert.Contains("\"State\"", json, StringComparison.Ordinal);
        var copy = JsonSerializer.Deserialize(json, WyrdJsonContext.Default.Envelope);
        Assert.Equal(trace, copy!.Trace);
        var instance = new StoredInstance("Process", "key", "trigger", "{}", 0, ProcessStatus.Running, [],
            System.Collections.Immutable.ImmutableDictionary<int, Guid>.Empty, [], 1, null, false, null, "fingerprint", [], 0, Trace: trace);
        var stored = JsonSerializer.Deserialize(JsonSerializer.Serialize(instance, WyrdJsonContext.Default.StoredInstance), WyrdJsonContext.Default.StoredInstance);
        Assert.Equal(trace, stored!.Trace);
        Assert.True(stored.Trace!.Context.IsRemote);
    }

    [Fact]
    public void an_invalid_carrier_does_not_throw_or_change_message_data()
    {
        using var listener = new Recording();
        using var scope = Tracing.Begin(new Envelope(Guid.Empty, MessageKind.Event, "event", "{}", Trace: new TraceContext("invalid")));
        Assert.NotNull(scope);
    }

    [Fact]
    public void a_disabled_source_does_not_capture_an_ambient_context()
    {
        using var ambient = new Activity("caller").Start();
        Assert.Null(Tracing.Capture());
        Assert.Null(Tracing.Begin(new Envelope(Guid.Empty, MessageKind.Command, "command", "{}")));
    }

    private static async Task<Snapshot> ShopScenario()
    {
        using var runtime = ShopModule.CreateRuntime();
        var probe = new Probe();
        runtime.Simulation = probe;
        await runtime.SendAsync(new Shop.Inventory.Restock("KEYBOARD", 10));
        await runtime.SendAsync(new Shop.Orders.PlaceOrder("A-1", "KEYBOARD", 1, 50m));
        await runtime.PublishAsync(new Shop.Contracts.PaymentConfirmed("A-1"));
        await runtime.SendAsync(new Shop.Orders.PlaceOrder("A-2", "KEYBOARD", 1, 2500m));
        runtime.FailNext<Shop.Payments.ChargePayment>();
        await runtime.SendAsync(new Shop.Orders.PlaceOrder("A-3", "KEYBOARD", 1, 50m));
        await runtime.AdvanceAsync(1.Seconds);
        await runtime.AdvanceAsync(15.Minutes);
        var state = new List<string>
        {
            JsonSerializer.Serialize(runtime.StateOf<Shop.Inventory.StockItem>("KEYBOARD"), ShopJson.Default.StockItem),
        };
        foreach (var id in OrderIds)
        {
            state.Add(JsonSerializer.Serialize(runtime.StateOf<Shop.Orders.Order>(id), ShopJson.Default.Order));
            state.Add(JsonSerializer.Serialize(runtime.StateOf<Shop.Payments.Payment>(id), ShopJson.Default.Payment));
        }

        return new Snapshot(runtime.Journal.Select(e => e.ToString()).ToArray(), state.ToArray(),
            OrderIds.Select(id => runtime.HistoryOf(ShopProcesses.OrderFulfillment, id).ToJson()).ToArray(),
            probe.Messages.Select(e => e with { Trace = null }).ToArray());
    }

    [Fact]
    public async Task an_integration_call_is_a_client_span_under_its_command()
    {
        using var listener = new Recording();
        var gateway = new FakeGateway { FailNext = 1 };
        var runtime = TestSystem.WithGateway(gateway, FluentProcess.OrderFulfillment);

        await runtime.SendAsync(new PlaceOrder("A-1", Quantity: 1, Amount: 50));
        await runtime.AdvanceAsync(1.Minutes);

        var calls = listener.Activities.Where(a => a.Kind == ActivityKind.Client).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, call => Assert.Equal(MessageTypes.NameOf(typeof(ChargePayment)), Tag(call, "wyrd.message.type")));
        Assert.Single(calls.Select(call => Tag(call, "wyrd.idempotency_key")).Distinct());
        Assert.Equal(["1", "2"], calls.Select(call => Tag(call, "wyrd.attempt")));
        Assert.Equal([ActivityStatusCode.Error, ActivityStatusCode.Unset], calls.Select(call => call.Status));
        var commands = listener.Activities.Where(a => Tag(a, "wyrd.message.kind") == "Command" && Tag(a, "wyrd.message.type") == Tag(calls[0], "wyrd.message.type"));
        Assert.All(calls, call => Assert.Contains(commands, command => command.SpanId == call.ParentSpanId));
    }

    [Fact]
    public async Task a_message_from_another_service_continues_its_trace()
    {
        using var listener = new Recording();
        var transport = new InMemoryTransport();
        var payments = new InMemoryRuntime().AddSlice(new ChargePaymentSlice()).AddPublication(new PaymentChargedV1Publication()).UseTransport(transport);
        var shipping = new InMemoryRuntime().AddSlice(new ShipOrderSlice())
            .AddProcess(Process.StartOn<PaymentChargedV1>(e => e.OrderId).Then(Shipping.ShipPaid));
        transport.Subscribe(shipping);

        await payments.SendAsync(new ChargePayment("A-1", 50));

        var sent = Assert.Single(transport.Sent);
        Assert.NotNull(sent.TraceParent);
        var charge = listener.Activities.Single(a => Tag(a, "wyrd.message.type") == MessageTypes.NameOf(typeof(ChargePayment)));
        var ship = listener.Activities.Single(a => Tag(a, "wyrd.message.type") == MessageTypes.NameOf(typeof(ShipOrder)));
        Assert.Equal(charge.TraceId, ship.TraceId);
    }

    [Fact]
    public async Task without_a_listener_nothing_is_traced_across_services()
    {
        var transport = new InMemoryTransport();
        var payments = new InMemoryRuntime().AddSlice(new ChargePaymentSlice()).AddPublication(new PaymentChargedV1Publication()).UseTransport(transport);

        await payments.SendAsync(new ChargePayment("A-1", 50));

        Assert.Null(Assert.Single(transport.Sent).TraceParent);
    }

    private static string? Tag(Activity activity, string name) => activity.GetTagItem(name)?.ToString();

    private sealed record Snapshot(string[] Journal, string[] State, string[] Histories, Envelope[] Messages);

    private sealed class Recording : IDisposable
    {
        public ConcurrentQueue<Activity> Activities { get; } = new();
        private readonly ActivityListener _listener;

        public Recording()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Wyrd",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Activities.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class FencingProbe : ISimulation
    {
        private readonly HashSet<Guid> _duplicated = [];
        public int PickNext(IReadOnlyList<Envelope> queue)
        {
            for (var i = 0; i < queue.Count; i++)
            {
                if (queue[i].Type != MessageTypes.NameOf(typeof(ChargePayment))) return i;
            }

            return 0;
        }

        public bool Duplicate(Envelope envelope) => envelope.Type == MessageTypes.NameOf(typeof(ChargePayment)) && _duplicated.Add(envelope.MessageId);
        public bool Fault(Envelope command, Type commandType) => false;
    }

    private sealed class Probe(bool duplicate = false) : ISimulation
    {
        public List<Envelope> Messages { get; } = [];
        private readonly HashSet<Guid> _duplicated = [];
        public int PickNext(IReadOnlyList<Envelope> queue)
        {
            Messages.Add(queue[0]);
            return 0;
        }

        public bool Duplicate(Envelope envelope) => duplicate && _duplicated.Add(envelope.MessageId);
        public bool Fault(Envelope command, Type commandType) => false;
    }
}
