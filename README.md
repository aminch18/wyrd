<div align="center">

# Wyrd

**Deterministic event-driven processes for .NET.**

*Event-driven that's no longer weird.*

</div>

> [!WARNING]
> 🚧 **This is a README from the future.** It describes the API Wyrd is being built towards.
> What works today is marked ✅; everything else is on the [roadmap](docs/ROADMAP.md).

---

Sagas, outboxes, retries and compensations are where event-driven systems quietly break: a duplicated
message charges a customer twice, a missing timeout leaves an order stuck forever, a compensation that
never runs leaves stock reserved. It all compiles. It all passes the happy-path tests.

**Wyrd makes the correct version the only one that compiles** — whether it's written by a human or by an AI agent.

- **You write pure functions.** Command + state in, events out. No `DbContext`, no publisher, no `await`.
- **Wyrd owns the side effects.** Outbox, idempotency, retries, timeouts and compensations are the framework's job, not yours.
- **The compiler enforces the rules.** Missing compensation, non-exhaustive match, slice-to-slice coupling: build errors, with fixes.
- **You review the process, not the plumbing.** Six declarative lines and a generated diagram instead of fifteen handler files.

## The three Norns

In Norse myth, *wyrd* — fate — is woven by three Norns. Wyrd is built on the same three:

| | Norn | Feature |
|---|---|---|
| ⏪ | **Urd** — *what was* | **Replay.** Export any production process instance and replay it locally, deterministically, as a test. |
| ▶️ | **Verdandi** — *what is* | **Runtime.** Transactional outbox and inbox, durable timeouts, retries and compensations. |
| ⏩ | **Skuld** — *what will be* | **Simulation.** Explore every combination of failure, retry, duplicate and timeout before you deploy. |

## A taste

### State as a closed set of cases

```csharp
[Union]
public abstract partial record Order
{
    public sealed record None : Order;
    public sealed record Placed(OrderId Id, Lines Lines) : Order;
    public sealed record Paid(OrderId Id, Money Amount) : Order;
    public sealed record Shipped(OrderId Id, TrackingCode Tracking) : Order;
}
```

Impossible states are unrepresentable. Add a case and forget to handle it: **build error**.

### A vertical slice is a pure function ✅

```csharp
[Slice]
public static class PlaceOrder
{
    public record Command(OrderId Id, CustomerId Customer, Lines Lines);

    public static Result<OrderPlaced, OrderError> Decide(Command cmd, Order state) =>
        state switch
        {
            Order.None when cmd.Lines.IsEmpty => OrderError.EmptyOrder,
            Order.None                        => new OrderPlaced(cmd.Id, cmd.Customer, cmd.Lines),
            _                                 => OrderError.AlreadyExists,
        };
}
```

### A process reads like the business describes it ✅

Fluent, by default:

```csharp
public static Process OrderFulfillment => Process
    .StartOn<OrderPlaced>()
    .Then(Stock.Reserve).Undo(Stock.Release)
    .Then(Payment.Charge).Retry(3.Times, Backoff.Exponential).Undo(Payment.Refund)
    .Await<PaymentConfirmed>().Within(15.Minutes)
    .Then(Shipping.Ship);
```

Or the opt-in pipe syntax — same model, same guarantees:

```csharp
using Wyrd.Pipe;
using static Wyrd.Pipe.Pipeline;

public static Process OrderFulfillment =>
    Start.On<OrderPlaced>()
      >> Step(Stock.Reserve).Undo(Stock.Release)
      >> Step(Payment.Charge).Retry(3.Times, Backoff.Exponential).Undo(Payment.Refund)
      >> Await<PaymentConfirmed>().Within(15.Minutes)
      >> Step(Shipping.Ship);
```

Pick one per repository with `wyrd_process_style = fluent | pipe | any` in `.editorconfig`.

### Living documentation ✅

Every process renders itself:

```mermaid
flowchart TD
    start(["OrderPlaced"])
    n1["Stock.Reserve"]
    n1 -. undo .-> n1u["Stock.Release"]
    start --> n1
    n2["Payment.Charge<br/>retry 3x exponential"]
    n2 -. undo .-> n2u["Payment.Refund"]
    n1 --> n2
    n3{{"await PaymentConfirmed<br/>within 15m"}}
    n2 --> n3
    n4["Shipping.Ship"]
    n3 --> n4
    n4 --> done(["completed"])
```

### Tests are just data

```csharp
[Fact]
public void cannot_place_an_empty_order() =>
    Given(new Order.None())
      .When(new PlaceOrder.Command(id, customer, Lines.Empty))
      .Then(OrderError.EmptyOrder);
```

### Skuld finds the future where it breaks

```
$ dotnet wyrd simulate

✗ OrderFulfillment: inconsistent final state
  OrderPlaced → StockReserved → PaymentCharged (duplicated) → Timeout(PaymentConfirmed)
  Payment refunded, but stock is still reserved.
```

### Urd replays the past

```
$ dotnet wyrd replay order-fulfillment/8f3c… --as-test
✓ Written tests/Regressions/OrderFulfillment_8f3c.cs
```

## Built for agents

Most event-driven code will be written by AI agents. Wyrd is designed so they can't get it wrong quietly:

- **Analyzer diagnostics written for a feedback loop**: what's wrong, why, and the exact fix — with code fixes.
- **A tiny surface**: pure deciders and a declarative process. The agent never touches the outbox.
- **Deterministic tooling**: a `dotnet wyrd` CLI and an MCP server to scaffold slices and steps.
- **Humans review intent**: the process definition and its diagram.

```
WYRD0012: 'Payment.Charge' has no compensation.
  Why: if a later step fails, the charge is never reverted.
  Fix: add .Undo(Payment.Refund) or mark it .NoCompensation("reason").
```

## What Wyrd guarantees — and what it doesn't

Wyrd guarantees **structural** correctness: effects go through the outbox, handlers are idempotent,
every step is compensated or explicitly not, every wait has a timeout, slices stay isolated.

It does **not** know your business rules. If you compute the tax wrong, Wyrd will deterministically compute it wrong.

## Status

Early spike. See the [roadmap](docs/ROADMAP.md).

```bash
dotnet test
```

Requires the .NET 10 SDK (C# 14).
