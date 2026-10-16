<div align="center">

# Wyrd

**Deterministic event-driven processes for .NET.**

*Event-driven that's no longer weird.*

</div>

> [!NOTE]
> **Early alpha.** Everything marked ✅ works today and is covered by tests. Everything marked 🔜 is designed
> and on the [roadmap](docs/ROADMAP.md).

---

Sagas, outboxes, retries and compensations are where event-driven systems quietly break: a duplicated
message charges a customer twice, a missing timeout leaves an order stuck forever, a compensation that
never runs leaves stock reserved. It all compiles. It all passes the happy-path tests.

**Wyrd makes the correct version the only one that compiles** — whether it's written by a human or by an AI agent.
DDD, event-driven and saga theory, applied correctly by construction, so teams can focus on what actually matters:
understanding the business and modelling it well.

- **You write pure functions.** Command + state in, event or error out. No `DbContext`, no publisher, no `await`.
- **Wyrd owns the side effects.** Retries, timeouts, compensations, idempotency and (soon) the outbox are the framework's job.
- **The process is data.** Six declarative lines that a reviewer can read, and that Wyrd can run, draw, simulate and replay.
- **Commands, domain events and integration events are different things** — and the compiler knows it.
- **Integration events are versioned contracts.** Breaking a published event is a build error, not a production incident.
- **Deterministic by design.** Same inputs, same outcome, every time.

## The three Norns

In Norse myth, *wyrd* — fate — is woven by three Norns. Wyrd is built on the same three:

| | Norn | Feature | |
|---|---|---|---|
| ⏩ | **Skuld** — *what will be* | **Simulation.** Explore every combination of failure, retry, duplicate and timeout before you deploy. | 🔜 |
| ⏪ | **Urd** — *what was* | **Replay.** Export a production process instance and replay it locally, deterministically, as a test. | 🔜 |
| ▶️ | **Verdandi** — *what is* | **Runtime.** Deterministic in-memory runtime today ✅; durable outbox, inbox and timeouts next. | 🚧 |

## Quick start

### 1. Messages say what they are ✅

```csharp
public sealed record ChargePayment(string OrderId, decimal Amount) : ICommand;            // an intention
public sealed record PaymentCharged(string OrderId, decimal Amount) : IDomainEvent;       // an internal fact

[IntegrationEvent("payments.payment-charged", Version = 1)]
public sealed record PaymentChargedV1(string OrderId, decimal Amount, string Currency)    // a public contract
    : IIntegrationEvent;
```

Commands are *sent* to exactly one owner. Domain events stay inside their context. Only integration events
cross boundaries — and only through an explicit `Publication`.

### 2. A slice owns a command and decides, purely ✅

```csharp
public sealed class ChargePaymentSlice : Slice<ChargePayment, Payment, PaymentCharged, PaymentError>
{
    public override object KeyOf(ChargePayment command) => command.OrderId;

    public override Result<PaymentCharged, PaymentError> Decide(ChargePayment command, Payment payment) => payment switch
    {
        Payment.None when command.Amount > 1_000m => PaymentError.CardDeclined,
        Payment.None                              => new PaymentCharged(command.OrderId, command.Amount),
        _                                         => PaymentError.AlreadyCharged,
    };
}
```

No `DbContext`, no publisher, no `await`: Wyrd loads the state, calls `Decide`, stores the event.
Expected failures are values (`Result`), not exceptions.

### 3. State is a closed set of cases ✅

```csharp
[Union]
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
}
```

Impossible states are unrepresentable: a `Refunded` payment has no amount to charge again.

### 4. The process reads like the business describes it ✅

```csharp
public static Process OrderFulfillment => Process
    .StartOn<OrderPlaced>(e => e.OrderId)
    .Then(Stock.Reserve).Undo(Stock.Release)
    .Then(Payments.Charge).Retry(3.Times, Backoff.Exponential).Undo(Payments.Refund)
    .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
    .Then(Shipping.Ship);
```

Steps are pure functions from the event that started the process to the next command:

```csharp
public static ChargePayment Charge(OrderPlaced order) => new(order.OrderId, order.Amount);
```

They are type-checked against the trigger: a step that doesn't accept `OrderPlaced` doesn't compile.

Prefer pipes? Opt in with a `using` — same model, same guarantees:

```csharp
using Wyrd.Pipe;
using static Wyrd.Pipe.Pipeline;

public static Process OrderFulfillment =>
    Start.On<OrderPlaced>(e => e.OrderId)
      >> Step(Stock.Reserve).Undo(Stock.Release)
      >> Step(Payments.Charge).Retry(3.Times, Backoff.Exponential).Undo(Payments.Refund)
      >> Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
      >> Step(Shipping.Ship);
```

### 5. Publish contracts deliberately ✅

```csharp
// The only place where an internal fact becomes a public one.
public sealed class PaymentChargedPublication : Publication<PaymentCharged, PaymentChargedV1>
{
    public override PaymentChargedV1 Map(PaymentCharged e) => new(e.OrderId, e.Amount, "EUR");
}
```

Refactor `PaymentCharged` at will: only the mapping has to keep producing a valid contract.

### 6. Run it ✅

```csharp
var shop = new InMemoryRuntime()
    .AddSlice(new ChargePaymentSlice())     // ...and the other slices
    .AddPublication(new PaymentChargedPublication())
    .AddProcess(Processes.OrderFulfillment);

shop.Send(new PlaceOrder("A-1005", "KEYBOARD", 1, 89m));
shop.Advance(15.Minutes); // the customer never completes 3-D Secure
```

```
[00:00:00] OrderFulfillment A-1005   started by OrderPlaced
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
[00:15:00] OrderFulfillment A-1005   compensated
```

The clock is virtual: a 15-minute timeout takes microseconds to test, and the journal is identical on every run.

| Behaviour | |
|---|---|
| Domain rejection (`Result` error) → compensate completed steps, in reverse order | ✅ |
| Transient failure → retry with backoff; retries exhausted → compensate | ✅ |
| `Await` timeout → compensate | ✅ |
| Duplicate trigger (at-least-once delivery) → ignored | ✅ |
| External events correlated by key | ✅ |
| Failed compensation → instance flagged as needing attention | ✅ |

## Versioned integration contracts ✅

Every integration event is snapshotted in `contracts.wyrd.txt`, next to your project and reviewed in pull requests:

```
payments.payment-charged v1 | Amount: decimal; Currency: string; OrderId: string
```

Wyrd's analyzers ship inside the package and compare your code with the snapshot on every build.
Change `Amount` from `decimal` to `double`, and:

```
error WYRD0201: Breaking change in integration event 'payments.payment-charged' v1: field 'Amount' changed
type from decimal to double. Existing subscribers would fail to read it. Publish it as v2 and keep v1 until
subscribers migrate, or revert the change.
```

| Change | Result |
|---|---|
| New integration event | `WYRD0200` — add the suggested line (IDE code fix: one click) |
| Add an optional field | `WYRD0202` — compatible; update the line (one click) |
| Remove, rename or retype a field; add a required one | `WYRD0201` — **build error** |
| Delete a published contract | `WYRD0203` — **build error**, unless its line is marked `retired` |
| Same name and version twice | `WYRD0204` — **build error** |
| Domain type inside an integration event | `WYRD0101` — **build error** |
| A type that is both a command and an event | `WYRD0001` — **build error** |

Moving to a new version is explicit: declare `PaymentChargedV2`, register its `Publication` next to V1's,
and both are published until subscribers migrate. Subscribers that already moved to V2 can also consume V1
through an `Upcaster<PaymentChargedV1, PaymentChargedV2>` — each subscriber receives every fact once, in its version.

| Rule | |
|---|---|
| A command decided by two slices | `WYRD0002` — **build error** |
| Events not in the past tense, commands that read like events | `WYRD0003`, `WYRD0004` — suggestions |

## Try the sample

[`samples/Shop`](samples/Shop) is a modular monolith: Orders, Inventory, Payments and Shipping slices, public
contracts in `Contracts/` with their `contracts.wyrd.txt`, and an order fulfillment process running six
real-world scenarios: happy path, flaky payment gateway, card declined, out of stock, abandoned 3-D Secure
and a double-clicked "Buy" button.

```bash
dotnet run --project samples/Shop
dotnet run --project samples/Shop -- --diagram   # print the process as Mermaid
```

## Living documentation ✅

Every process renders itself (`process.ToMermaid()`):

```mermaid
flowchart TD
    start(["OrderPlaced"])
    n1["Stock.Reserve"]
    n1 -. undo .-> n1u["Stock.Release"]
    start --> n1
    n2["Payments.Charge<br/>retry 3x exponential"]
    n2 -. undo .-> n2u["Payments.Refund"]
    n1 --> n2
    n3{{"await PaymentConfirmed<br/>within 15m"}}
    n2 --> n3
    n4["Shipping.Ship"]
    n3 --> n4
    n4 --> done(["completed"])
```

## Coming next

### Skuld finds the future where it breaks 🔜

```
$ dotnet wyrd simulate

✗ OrderFulfillment: inconsistent final state
  OrderPlaced → StockReserved → PaymentCharged (duplicated) → Timeout(PaymentConfirmed)
  Payment refunded, but stock is still reserved.
```

### Tests are just data 🔜

```csharp
[Fact]
public void cannot_charge_twice() =>
    Given(new Payment.Charged(89m))
      .When(new ChargePayment("A-1", 89m))
      .Then(PaymentError.AlreadyCharged);
```

### Built for agents 🔜

Most event-driven code will be written by AI agents. Wyrd is designed so they can't get it wrong quietly:
a tiny pure surface, and diagnostics written for a feedback loop — *what* is wrong, *why* it matters and
*how* to fix it, like `WYRD0201` above. Next: process rules (missing compensation, `Await` without timeout)
and deterministic tooling (`dotnet wyrd` CLI and an MCP server).

## What Wyrd guarantees — and what it doesn't

Wyrd guarantees **structural** correctness: effects are owned by the framework, every step is compensated or
explicitly not, every wait can time out, duplicates are detected, failures are visible.

It does **not** know your business rules. If you compute the tax wrong, Wyrd will deterministically compute it wrong.

## Repository layout

```
src/Wyrd/                  the library
src/Wyrd.Analyzers/        Roslyn analyzers, shipped inside the Wyrd package
src/Wyrd.Analyzers.CodeFixes/  IDE code fixes, shipped inside the Wyrd package
tests/Wyrd.Tests/          unit and runtime tests
tests/Wyrd.Analyzers.Tests analyzer tests
samples/Shop/              a runnable, vertically sliced example
docs/                      roadmap and design notes
```

## License

[MIT](LICENSE). Free for any use, forever.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Requires the .NET 10 SDK (C# 14).

```bash
dotnet test
```
