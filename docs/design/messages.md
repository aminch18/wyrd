# Design: messages — commands, domain events, integration events

> Status: **implemented** (taxonomy, publications, contract snapshots, upcasters and the analyzers). AsyncAPI comes
> from the `dotnet wyrd` tool. This is a core pillar of Wyrd: the theory applied correctly by construction,
> so developers spend their time on the business, not on re-learning messaging pitfalls.

## Three kinds of message, three different contracts

| | **Command** | **Domain event** | **Integration event** |
|---|---|---|---|
| Meaning | An intention: *please do this* | A fact inside one bounded context | A fact published to other contexts |
| Name | Imperative: `ChargePayment` | Past tense: `PaymentCharged` | Past tense, namespaced: `payments.payment-charged` |
| Can be rejected? | Yes, that's the point | No, it already happened | No, it already happened |
| Who handles it | **Exactly one** owner | The owning context only | **Any number** of subscribers |
| Visibility | Sender ↔ owner | Private to the context | **Public contract** |
| Can change freely? | Owner-defined, versioned with the owner | **Yes** — it's internal | **No — versioned, compatibility-checked** |

Mixing them up is the most common source of coupling in event-driven systems: a service subscribes to
another one's internal domain events, the owner renames a field, and production breaks somewhere else.

## How Wyrd enforces it

### Types say what a message is

```csharp
public sealed record ChargePayment(string OrderId, decimal Amount) : ICommand;
public sealed record PaymentCharged(string OrderId, decimal Amount) : IDomainEvent;

[IntegrationEvent("payments.payment-charged", Version = 1)]
public sealed record PaymentChargedV1(string OrderId, decimal Amount, string Currency) : IIntegrationEvent;
```

### Rules (analyzers, build errors)

| Rule | Why |
|---|---|
| A command has exactly one slice handling it | Two owners for one intention means nobody owns it |
| Commands are *sent*, events are *published*; never the other way round | An event can't be refused; a command can't be broadcast |
| A slice returns domain events only | Internal facts stay internal |
| Integration events are produced only by an explicit **publication mapping** | The public contract is a deliberate translation, not a leak |
| Integration events contain only primitives and other contract types | No domain types leak through the contract |
| Other contexts can subscribe only to integration events | Nobody couples to someone else's internals |
| Naming: commands imperative, events past tense | Language makes intent obvious; the analyzer suggests a name |

### Publication mapping: the boundary is explicit

```csharp
public static class PaymentsPublications
{
    // The only place where internal facts become public ones.
    public static PaymentChargedV1 Publish(PaymentCharged e) => new(e.OrderId, e.Amount, "EUR");
}
```

The domain event can be refactored at will. Only this mapping has to keep producing a valid contract.

## Integration event versioning

### Contract snapshots

Every integration event's schema is snapshotted into the repository (e.g. `contracts/payments.payment-charged.v1.json`),
generated at build time, reviewed in pull requests like any other file.

On build, the current type is compared with the snapshot:

| Change | Result |
|---|---|
| Add an optional field | ✅ compatible; the snapshot is updated |
| Remove or rename a field, change a type, make a field required | ❌ **build error** — publish a new version |
| Change the event name/topic | ❌ build error |

```
WYRD0201: Breaking change in integration event 'payments.payment-charged' v1.
  Field 'Amount' changed type: decimal -> Money.
  Why: existing subscribers would fail to deserialize it.
  Fix: create PaymentChargedV2 and keep publishing V1 until subscribers migrate,
       or revert the change.
```

### Evolving: new version + upcaster + transition window

```csharp
[IntegrationEvent("payments.payment-charged", Version = 2)]
public sealed record PaymentChargedV2(string OrderId, Money Amount) : IIntegrationEvent;

public static class PaymentChargedUpcasters
{
    public static PaymentChargedV2 Up(PaymentChargedV1 v1) => new(v1.OrderId, new Money(v1.Amount, v1.Currency));
}
```

- **Publishers** declare which versions they publish; during a transition they publish both (`.AlsoPublish<PaymentChargedV1>()`).
- **Subscribers** handle the version they understand; older versions are upcast automatically.
- Removing an old version is an explicit, reviewable change (`.Retire<PaymentChargedV1>()`), never an accident.

### Documentation for free

The contract catalog generates **AsyncAPI**: every integration event, its versions, who publishes and who subscribes.

## Why this is valuable on its own

Contract snapshots and the message rules are **pure build-time tooling**: they need no runtime, no broker,
no database. They can ship early and be useful even to teams not yet running Wyrd's runtime.
