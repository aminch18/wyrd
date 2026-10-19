export interface DocSection {
    id: string
    title: string
    subtitle: string
    group: 'Why' | 'Behavior' | 'Guides' | 'Reference'
}

export const DOC_SECTIONS: DocSection[] = [
    {
        id: 'overview',
        group: 'Why',
        title: 'Overview & Philosophy',
        subtitle: 'Why Wyrd exists and what it guarantees by construction.',
    },
    {
        id: 'semantics',
        group: 'Behavior',
        title: 'How Wyrd behaves',
        subtitle:
            'The rules behind the API, each marked implemented or planned.',
    },
    {
        id: 'decisions',
        group: 'Behavior',
        title: 'Design decisions',
        subtitle: 'Problem, decision, what it gives you and what it costs.',
    },
    {
        id: 'from-ddd',
        group: 'Guides',
        title: 'From DDD to Wyrd',
        subtitle:
            'Follow the same business rule from a mutable aggregate to a pure decision.',
    },
    {
        id: 'read-models',
        group: 'Guides',
        title: 'Read models',
        subtitle: 'Pure projections, screen models, ordering and queries.',
    },
    {
        id: 'quick-start',
        group: 'Guides',
        title: 'Quick start',
        subtitle: 'Install and run the sample.',
    },
    {
        id: 'api-reference',
        group: 'Reference',
        title: 'API reference',
        subtitle: 'Source-checked signatures, examples and analyzer rules.',
    },
    {
        id: 'messages',
        group: 'Reference',
        title: 'Messages & Slices',
        subtitle: 'Commands, domain events, closed states, and pure deciders.',
    },
    {
        id: 'processes',
        group: 'Reference',
        title: 'Declarative Processes',
        subtitle:
            'Sagas as data: fluent & pipe syntax, retries, timeouts, compensations.',
    },
    {
        id: 'simulation',
        group: 'Reference',
        title: 'Simulation (Skuld)',
        subtitle:
            'Hostile futures, invariants, and exact replay of any broken run.',
    },
    {
        id: 'contracts',
        group: 'Reference',
        title: 'Versioned Contracts',
        subtitle:
            'Integration events, snapshots, upcasters, and Roslyn analyzers.',
    },
    {
        id: 'testing-aot',
        group: 'Reference',
        title: 'Testing & Native AOT',
        subtitle:
            'Given/When/Then business specs, virtual time, and trim-safe serialization.',
    },
    {
        id: 'tool',
        group: 'Reference',
        title: 'CLI & generated docs',
        subtitle:
            'Export AsyncAPI contracts and Mermaid process diagrams with Wyrd.Tool.',
    },
    {
        id: 'roadmap',
        group: 'Reference',
        title: 'Roadmap',
        subtitle: 'Where Wyrd is today and what is coming next.',
    },
]

export const SNIPPETS = {
    fluentProcess: `public static Process OrderFulfillment => Process
    .StartOn<OrderPlaced>(e => e.OrderId)
    .Then(Stock.Reserve).Retry(5.Times).Undo(Stock.Release)
    .Then(Payments.Charge).Retry(5.Times, Backoff.Exponential).Undo(Payments.Refund)
    .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
    .Then(Shipping.Ship).Retry(5.Times)
    .OnCompensated(Orders.Cancel).Retry(5.Times);`,

    pipeProcess: `using Wyrd.Pipe;
using static Wyrd.Pipe.Pipeline;

public static Process OrderFulfillment =>
    Start.On<OrderPlaced>(e => e.OrderId)
      >> Step(Stock.Reserve).Retry(5.Times).Undo(Stock.Release)
      >> Step(Payments.Charge).Retry(5.Times, Backoff.Exponential).Undo(Payments.Refund)
      >> Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
      >> Step(Shipping.Ship).Retry(5.Times)
      >> OnCompensated(Orders.Cancel).Retry(5.Times);`,

    messages: `// An intention sent to exactly one owner
public sealed record ChargePayment(string OrderId, decimal Amount) : ICommand;

// An internal fact inside one bounded context
public sealed record PaymentCharged(string OrderId, decimal Amount) : IDomainEvent;

// A versioned public contract across boundaries
[IntegrationEvent("payments.payment-charged", Version = 1)]
public sealed record PaymentChargedV1(string OrderId, decimal Amount, string Currency)
    : IIntegrationEvent;`,

    slice: `public sealed class ChargePaymentSlice : Slice<ChargePayment, Payment, PaymentCharged, PaymentError>
{
    public override object KeyOf(ChargePayment command) => command.OrderId;

    public override Result<PaymentCharged, PaymentError> Decide(ChargePayment command, Payment payment) => payment switch
    {
        Payment.None when command.Amount > 1_000m => PaymentError.CardDeclined,
        Payment.None                              => new PaymentCharged(command.OrderId, command.Amount),
        Payment.Charged or Payment.Refunded       => PaymentError.AlreadyCharged,
    };
}`,

    stateUnion: `[Union]
public abstract record Payment : IState<Payment>
{
    public static Payment Initial { get; } = new None();

    public Payment Evolve(IDomainEvent domainEvent) => (this, domainEvent) switch
    {
        (None, PaymentCharged charged) => new Charged(charged.Amount),
        (Charged, PaymentRefunded)     => new Refunded(),
        _                              => this,
    };

    public sealed record None : Payment;
    public sealed record Charged(decimal Amount) : Payment;
    public sealed record Refunded : Payment;
}`,

    publication: `// The only place where an internal fact becomes a public contract.
public sealed class PaymentChargedPublication : Publication<PaymentCharged, PaymentChargedV1>
{
    public override PaymentChargedV1 Map(PaymentCharged e) => new(e.OrderId, e.Amount, "EUR");
}`,

    runtimeRun: `var shop = new InMemoryRuntime()
    .AddSlice(new ChargePaymentSlice())     // ...and the other slices
    .AddPublication(new PaymentChargedPublication())
    .AddProcess(Processes.OrderFulfillment);

await shop.SendAsync(new PlaceOrder("A-1005", "KEYBOARD", 1, 89m));
await shop.AdvanceAsync(15.Minutes); // customer never completes 3-D Secure`,

    journalOutput: `[00:00:00] OrderFulfillment A-1005   started by OrderPlaced
[00:00:00] OrderFulfillment A-1005   -> ReserveStock
[00:00:00] OrderFulfillment A-1005   ok StockReserved
[00:00:00] OrderFulfillment A-1005   -> ChargePayment
[00:00:00] OrderFulfillment A-1005   ok PaymentCharged
[00:00:00] OrderFulfillment A-1005   awaiting PaymentConfirmed (within 15m)
[00:15:00] OrderFulfillment A-1005   timeout: PaymentConfirmed not received in time
[00:15:00] OrderFulfillment A-1005   compensating
[00:15:00] OrderFulfillment A-1005   -> RefundPayment
[00:15:00] OrderFulfillment A-1005   ok PaymentRefunded
[00:15:00] OrderFulfillment A-1005   -> ReleaseStock
[00:15:00] OrderFulfillment A-1005   ok StockReleased
[00:15:00] OrderFulfillment A-1005   compensated`,

    contractError: `# contracts.wyrd.txt (reviewed in PRs)
payments.payment-charged v1 | Amount: decimal; Currency: string; OrderId: string

# If you change Amount from decimal to double:
error WYRD0201: Breaking change in integration event 'payments.payment-charged' v1:
field 'Amount' changed type from decimal to double. Existing subscribers would fail
to read it. Publish it as v2 and keep v1 until subscribers migrate, or revert the change.`,

    testing: `[Fact]
public void an_order_is_never_charged_twice() =>
    Scenario.For(new ChargePaymentSlice())
        .Given(new PaymentCharged("A-1", 89m))      // what already happened
        .When(new ChargePayment("A-1", 89m))        // what is asked
        .ThenRejected(PaymentError.AlreadyCharged); // what the business decides

[Fact]
public void a_shipped_order_shows_as_shipped() =>
    Scenario.For(new OrderSummaries())
        .Given(new OrderPlaced("A-1", "KEYBOARD", 1, 89m), new OrderShipped("A-1"))
        .Then("A-1", new OrderSummary("A-1", "KEYBOARD", 1, 89m, "Shipped"));`,

    skuld: `private static readonly Invariant StockIsConserved = new("stock is conserved", shop =>
{
    var shipped = shop.Views<OrderSummary>().Where(o => o.Status == "Shipped").Sum(o => o.Quantity);
    var available = shop.StateOf<StockItem>("KEYBOARD").Available;
    return available == 5 - shipped ? null : $"{available} available, expected {5 - shipped}";
});

[Fact]
public async Task the_business_holds_in_every_future()
{
    var report = await Skuld.SimulateAsync(
        ShopModule.CreateRuntime,
        async shop =>
        {
            await shop.SendAsync(new Restock("KEYBOARD", 5));
            await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 1, 89m));
            await shop.SendAsync(new PlaceOrder("A-2", "KEYBOARD", 2, 2_500m)); // card declined
            await shop.PublishAsync(new PaymentConfirmed("A-1"));
        },
        [.. Invariant.Defaults, StockIsConserved],
        new SimulationOptions { Runs = 200, FaultRate = 0.1, DuplicateRate = 0.1, ReorderRate = 0.3 });

    report.ThrowIfViolated();
}`,

    skuldReport: `Skuld explored 200 futures: 42 broke an invariant.

✗ "valid orders go through" broke in 42 futures. First: seed 9.
  Smallest future that breaks it: 1 perturbation (4 replays; smallest found within budget).
  A-1 didn't go through because of a transient failure
  Perturbations: ReserveStock (A-1) failed transiently
  Journal:
    [00:00:00] -                A-1      -> PlaceOrder
    [00:00:00] -                A-1      ok OrderPlaced
    [00:00:00] Fragile          A-1      started by OrderPlaced
    [00:00:00] Fragile          A-1      -> ReserveStock
    [00:00:00] skuld                     ReserveStock (A-1) failed transiently
    [00:00:00] Fragile          A-1      transient failure, no retry policy
    [00:00:00] Fragile          A-1      SUSPENDED: Stock.Reserve failed after 1 attempt, needs attention
    [00:00:00] -                A-2      -> PlaceOrder
    [00:00:00] -                A-2      ok OrderPlaced
    [00:00:00] Fragile          A-2      started by OrderPlaced
    [00:00:00] Fragile          A-2      -> ReserveStock
    [00:00:00] Fragile          A-2      ok StockReserved
    [00:00:00] Fragile          A-2      -> ChargePayment
    [00:00:00] Fragile          A-2      rejected Declined
    [00:00:00] Fragile          A-2      compensating
    [00:00:00] Fragile          A-2      -> ReleaseStock
    [00:00:00] Fragile          A-2      ok StockReleased
    [00:00:00] Fragile          A-2      compensated
  Replay it: Skuld.SimulateAsync(..., options.Only(9))`,

    aotJson: `[WyrdJsonContext]
[JsonSerializable(typeof(ChargePayment))]
[JsonSerializable(typeof(Payment))] // unions are stored polymorphically
public partial class ShopJson : JsonSerializerContext;

var shop = new InMemoryRuntime(ShopJson.Default);`,
}

export const ANALYZER_RULES = [
    {
        code: 'WYRD0001',
        severity: 'Error',
        description: 'A type cannot be both a command and an event.',
    },
    {
        code: 'WYRD0002',
        severity: 'Error',
        description: 'A command must have a single owning slice.',
    },
    {
        code: 'WYRD0003 / 0004',
        severity: 'Info',
        description:
            'Events should be past tense; commands should be imperative.',
    },
    {
        code: 'WYRD0011 / 0012',
        severity: 'Warning',
        description:
            'Process rules: every step says how it is undone or .NoCompensation("why") (WYRD0011), every wait has .Within (WYRD0012).',
    },
    {
        code: 'WYRD0014',
        severity: 'Warning',
        description:
            'Every step declares its retry policy, .Retry(...) or .NoRetry("why"). There is no default. Replaces the removed WYRD0010.',
    },
    {
        code: 'WYRD0013',
        severity: 'Warning',
        description:
            'The process syntax a repository picks (wyrd_process_style), with a one-click fluent ⇄ pipe conversion.',
    },
    {
        code: 'WYRD0020',
        severity: 'Error',
        description:
            'A switch over a [Union] must handle every case; when it does, no discard is needed.',
    },
    {
        code: 'WYRD0101',
        severity: 'Error',
        description:
            'Domain types cannot leak inside a public integration event contract.',
    },
    {
        code: 'WYRD0200 / 0202',
        severity: 'Warning',
        description:
            'New integration event or compatible optional field added — one-click IDE code fix updates contracts.wyrd.txt.',
    },
    {
        code: 'WYRD0201 / 0203',
        severity: 'Error',
        description:
            'Breaking field change or deleted contract without retirement.',
    },
    {
        code: 'WYRD0300–0302',
        severity: 'Error',
        description:
            'Keeps [WyrdJsonContext] complete and collision-free for Native AOT.',
    },
]
