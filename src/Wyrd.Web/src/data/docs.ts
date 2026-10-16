export interface DocSection {
    id: string
    title: string
    subtitle: string
}

export const DOC_SECTIONS: DocSection[] = [
    {
        id: 'overview',
        title: 'Overview & Philosophy',
        subtitle: 'Why Wyrd exists and what it guarantees by construction.',
    },
    {
        id: 'from-ddd',
        title: 'From DDD to Wyrd',
        subtitle:
            'Follow the same business rule from a mutable aggregate to a pure decision.',
    },
    {
        id: 'quick-start',
        title: 'Quick start',
        subtitle: 'Install and run the sample.',
    },
    {
        id: 'messages',
        title: 'Messages & Slices',
        subtitle: 'Commands, domain events, closed states, and pure deciders.',
    },
    {
        id: 'processes',
        title: 'Declarative Processes',
        subtitle:
            'Sagas as data: fluent & pipe syntax, retries, timeouts, compensations.',
    },
    {
        id: 'contracts',
        title: 'Versioned Contracts',
        subtitle:
            'Integration events, snapshots, upcasters, and Roslyn analyzers.',
    },
    {
        id: 'testing-aot',
        title: 'Testing & Native AOT',
        subtitle:
            'Given/When/Then business specs, virtual time, and trim-safe serialization.',
    },
    {
        id: 'tool',
        title: 'CLI & generated docs',
        subtitle:
            'Export AsyncAPI contracts and Mermaid process diagrams with Wyrd.Tool.',
    },
    {
        id: 'roadmap',
        title: 'Roadmap',
        subtitle: 'Where Wyrd is today and what is coming next.',
    },
]

export const SNIPPETS = {
    fluentProcess: `public static Process OrderFulfillment => Process
    .StartOn<OrderPlaced>(e => e.OrderId)
    .Then(Stock.Reserve).Undo(Stock.Release)
    .Then(Payments.Charge).Retry(3.Times, Backoff.Exponential).Undo(Payments.Refund)
    .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
    .Then(Shipping.Ship)
    .OnCompensated(Orders.Cancel);`,

    pipeProcess: `using Wyrd.Pipe;
using static Wyrd.Pipe.Pipeline;

public static Process OrderFulfillment =>
    Start.On<OrderPlaced>(e => e.OrderId)
      >> Step(Stock.Reserve).Undo(Stock.Release)
      >> Step(Payments.Charge).Retry(3.Times, Backoff.Exponential).Undo(Payments.Refund)
      >> Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
      >> Step(Shipping.Ship);`,

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
        _                                         => PaymentError.AlreadyCharged,
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
