# Design: Wyrd in a distributed system

> Status: **proposal**. Nothing in this document is implemented yet, except where marked ✅.

## Goal

The same user code runs in memory (tests, simulation, replay) and across services (production).
Teams write **decisions**; Wyrd owns **delivery, persistence, retries, timeouts, compensation and idempotency**.

| Teams write | Wyrd does |
|---|---|
| Message contracts | Routing commands to the owning service |
| `Decide` / `Evolve` (pure) | Loading and saving state, atomically with outgoing messages (outbox) |
| The process definition | Persisting where every process instance is, surviving restarts |
| Integrations with external APIs (the only I/O) | Deduplication (inbox), retries, durable timeouts, compensation |

## What a team writes

### 1. Contracts (shared between services)

Plain records. No logic, no Wyrd base classes beyond a marker. Commands, domain events and integration events
are different kinds of message with different rules — see [messages.md](messages.md).

```csharp
// Shop.Contracts
public sealed record ChargePayment(string OrderId, decimal Amount) : ICommand;
public sealed record PaymentCharged(string OrderId, decimal Amount) : IEvent;
public enum PaymentError { CardDeclined, AlreadyCharged }
```

### 2. Slices (inside the owning service)

The decision moves out of the contract and into the service that owns it.

```csharp
// Payments service
public sealed class ChargePaymentSlice : ISlice<ChargePayment, Payment, PaymentCharged, PaymentError>
{
    public static object Key(ChargePayment cmd) => cmd.OrderId;

    public static Result<PaymentCharged, PaymentError> Decide(ChargePayment cmd, Payment payment) => payment switch
    {
        Payment.None when cmd.Amount > 1_000m => PaymentError.CardDeclined,
        Payment.None                          => new PaymentCharged(cmd.OrderId, cmd.Amount),
        _                                     => PaymentError.AlreadyCharged,
    };
}
```

> **Change from today:** the current `ICommand<TSelf, ...>` puts `Decide` on the command itself. That couples
> the contract to the owner's logic, which cannot work across services. Proposal: a single shape everywhere
> (contract + slice), also in modular monoliths, where it is good vertical-slice hygiene anyway.

### 3. Integrations (the only place with I/O)

Charging a real card means calling a payment provider. That cannot be pure, so it gets its own, narrow shape:

```csharp
public sealed class StripeCharge(StripeClient stripe) : IIntegration<ChargeCard, CardCharged, PaymentError>
{
    public async Task<Result<CardCharged, PaymentError>> Run(ChargeCard cmd, IntegrationContext ctx, CancellationToken ct)
    {
        var charge = await stripe.Charges.CreateAsync(..., idempotencyKey: ctx.IdempotencyKey, ct);
        return charge.Paid ? new CardCharged(cmd.OrderId, charge.Id) : PaymentError.CardDeclined;
    }
}
```

- Wyrd supplies a stable **idempotency key** (the message id), so retries never charge twice.
- The **outcome is recorded as an event**. Replay (Urd) and simulation (Skuld) use the recorded or simulated
  outcome and never call the real API. This is what keeps the system deterministic.
- Exceptions are transient failures (retried); `Result` errors are business outcomes (compensated).

### 4. The process (inside the orchestrating service) — unchanged ✅

```csharp
public static Process OrderFulfillment => Process
    .StartOn<OrderPlaced>(e => e.OrderId)
    .Then(Stock.Reserve).Undo(Stock.Release)
    .Then(Payments.Charge).Retry(3.Times).Undo(Payments.Refund)
    .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
    .Then(Shipping.Ship);
```

### 5. Hosting

```csharp
builder.Services.AddWyrd(wyrd => wyrd
    .UsePostgres(connectionString)
    .UseRabbitMq(rabbitConnectionString)
    .AddSlicesFrom<ChargePaymentSlice>()     // discovered at compile time by a source generator
    .AddProcess(Processes.OrderFulfillment));

app.MapPost("/orders", async (PlaceOrderRequest req, IWyrd wyrd) =>
    await wyrd.SendAsync(req.ToCommand()) switch
    {
        Decision.Accepted   => Results.Accepted(),
        Decision.Rejected r => Results.Problem(r.Error.ToString(), statusCode: 409),
    });

app.MapPost("/webhooks/payments", (PspWebhook hook, IWyrd wyrd) =>
    wyrd.PublishAsync(new PaymentConfirmed(hook.OrderId)));
```

## How it works

### Executing a step

```
Orders (orchestrator)                       broker                    Payments (owner)
─────────────────────                       ──────                    ────────────────
tx: instance.position = 2
    outbox += ChargePayment(id=m1)  ──────►  ChargePayment(m1)  ──────►  tx: inbox += m1
                                                                            state, events
                                                                            outbox += Reply(m1, Accepted)
tx: inbox += reply
    instance.position = 3           ◄──────  Reply(m1, ...)     ◄──────
```

- **Replies are a framework envelope**: `Accepted(event)`, `Rejected(error)` or `Faulted(reason)`.
  Teams never define `PaymentRejected`-style events by hand.
- If the slice is hosted locally, the step runs in-process in the same transaction (no broker hop).

### Failure model

| Situation | Behaviour |
|---|---|
| Orchestrator crashes after committing, before publishing | Outbox relay publishes on restart |
| Message delivered twice | Receiver's inbox returns the stored reply; `Decide` runs once |
| No reply in time / `Faulted` | Orchestrator re-sends **the same message id** (so it is deduplicated) per the step's `Retry` |
| Retries exhausted | Compensation |
| `Rejected` | Compensation, no retry (a pure decision would answer the same) |
| A compensation fails after its retries | Instance → `Failed`, surfaced as a metric/alert; never silently dropped |
| Awaited event arrives **before** the instance reaches the `Await` | **Parked** by `(type, key)` and consumed when the instance gets there |
| Messages arrive out of order | Allowed. Nothing assumes ordering; parking covers awaits |
| Two pods handle the same instance | Optimistic concurrency on the instance row; the loser retries |

### Persistence (per service, e.g. PostgreSQL)

| Table | Purpose |
|---|---|
| `wyrd_state` | Slice state per `(state type, key)`, with version |
| `wyrd_events` | Accepted events (history for Urd) |
| `wyrd_outbox` | Messages to publish, written in the business transaction |
| `wyrd_inbox` | Processed message ids and their stored replies |
| `wyrd_instances` | Process instances: position, status, completed steps, deadline, version |
| `wyrd_parked` | Early events waiting for an `Await` |

Timeouts are a `deadline` column polled with `FOR UPDATE SKIP LOCKED`, so they survive restarts and scale out.

## Guarantees

- **No lost messages**: state changes and outgoing messages commit together (outbox).
- **Effectively once per decision**: at-least-once delivery + inbox deduplication + stable message ids.
- **Durable progress and timers**: a process continues from its last committed step after any crash.
- **Every failure ends visibly**: completed, compensated, or `Failed` with an alert.

**Not guaranteed:** isolation between processes (others may observe intermediate states, such as reserved
stock — the usual saga trade-off), and anything about the correctness of business rules.

## Testing

- **In memory:** the whole multi-service flow runs in one process, with the same user code, with no Docker. ✅ (current runtime)
- **Skuld:** deterministic simulation of crashes at every commit point, redelivery, reordering, delays and
  timeouts — the *deterministic simulation testing* approach of FoundationDB and TigerBeetle, applied to business processes.

## Positioning

- **MassTransit / NServiceBus / Wolverine**: messaging runtimes; you write handlers and sagas imperatively.
  Wyrd's process is declarative data, decisions are pure, and the effects are not user code.
- **Temporal**: durable execution of imperative workflow code, which requires a Temporal server.
  Wyrd runs on your own database and broker, and the process is a definition that can be read, drawn, simulated and versioned.

## Open questions

1. Routing convention: by contract namespace/assembly, or explicit `.Route<ChargePayment>("payments")`?
2. Steps that need data from earlier steps (process state), not only the trigger.
3. Serialization: System.Text.Json source-generated contexts (AOT) — generated by Wyrd or supplied by the user?
4. First transport: RabbitMQ only, or RabbitMQ + Azure Service Bus?
5. Event-sourced slice state as an option alongside state-stored.
