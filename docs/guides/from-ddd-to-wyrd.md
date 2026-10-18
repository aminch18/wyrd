# From classic DDD to Wyrd

## Start with the rule, not the framework

Suppose the business says: a placed order may be cancelled once. Cancelling an already cancelled order is rejected. Cancelling an order that does not exist is rejected too.

The question is the same in either design: given the current order, is cancellation allowed? What changes is where the answer is represented and how its consequences are executed. This guide uses a deliberately small example so the architectural change is visible. It is not a claim that every order domain has only two states or that every cancellation is reversible.

## The familiar aggregate method

An object-oriented aggregate can encapsulate its state and enforce the rule through a method. This simplified model assumes that an order has already been placed when it is constructed:

```csharp
public enum OrderStatus { Placed, Cancelled }

public sealed record OrderCancelled(string OrderId);

public sealed class Order
{
    public string Id { get; }
    public OrderStatus Status { get; private set; }

    public Order(string id)
    {
        Id = id;
        Status = OrderStatus.Placed;
    }

    public OrderCancelled Cancel()
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Order is already cancelled.");

        Status = OrderStatus.Cancelled;
        return new OrderCancelled(Id);
    }
}
```

An application handler loads this aggregate through a repository, rejects a missing order, calls `Cancel`, and persists the result within the application's transaction policy. Many well-designed applications already separate this domain method from external effects. DDD does not require infrastructure inside an aggregate, and it does not require exceptions for business rejections; those are choices in this example.

The repository and transaction setup are not the business rule. They are the execution context the application supplies around it.

## Express the same decision in Wyrd

In Wyrd, the request is a command, the outcome is a domain event or a named error, and the decision receives the current state explicitly. The following slice is the cancellation rule from the Shop sample:

```csharp
using System.Text.Json.Serialization;
using Wyrd;

public sealed record CancelOrder(string OrderId) : ICommand;
public sealed record OrderCancelled(string OrderId) : IDomainEvent;

public enum OrderError
{
    AlreadyCancelled,
    NotFound,
}

public sealed class CancelOrderSlice
    : Slice<CancelOrder, Order, OrderCancelled, OrderError>
{
    public override object KeyOf(CancelOrder command) => command.OrderId;

    public override Result<OrderCancelled, OrderError> Decide(
        CancelOrder command,
        Order order) => order switch
    {
        Order.Placed => new OrderCancelled(command.OrderId),
        Order.Cancelled => OrderError.AlreadyCancelled,
        _ => OrderError.NotFound,
    };
}
```

The state below is a reduced version of the Shop state: it omits the placed order's SKU and quantity to keep the cancellation example focused. `OrderPlaced` is included to show how the order reaches the state cancellation accepts:

```csharp
public sealed record OrderPlaced(string OrderId) : IDomainEvent;

[Union]
[JsonDerivedType(typeof(Order.None), "none")]
[JsonDerivedType(typeof(Order.Placed), "placed")]
[JsonDerivedType(typeof(Order.Cancelled), "cancelled")]
public abstract record Order : IState<Order>
{
    public static Order Initial { get; } = new None();

    public Order Evolve(IDomainEvent domainEvent) => (this, domainEvent) switch
    {
        (None, OrderPlaced) => new Placed(),
        (Placed, OrderCancelled) => new Cancelled(),
        _ => this,
    };

    public sealed record None : Order;
    public sealed record Placed : Order;
    public sealed record Cancelled : Order;
}
```

`Decide` checks whether an intention is valid. `Evolve` applies an accepted fact. The runtime loads the state, invokes the decision, applies the event, and commits the resulting changes. A rejection produces no successful domain event. `Order.None` represents the initial state for a key with no stored order, making the missing-order case explicit in the decision.

The derived-type attributes preserve the concrete state case across JSON serialization. These excerpts describe the model rather than a complete host. A source-generated JSON context also needs the message types and union cases declared; see the [serialization walkthrough](https://github.com/aminch18/wyrd#native-aot-source-generated-json-) and the repository's [ShopJson example](https://github.com/aminch18/wyrd/blob/main/samples/Shop/ShopJson.cs) for that setup.

## Map the concepts, not just the class names

| Familiar DDD concept | Expression in Wyrd | Responsibility that stays yours |
| --- | --- | --- |
| Aggregate root | A keyed state model and the slices that decide changes to it | Choose the consistency boundary and preserve its invariants. |
| Aggregate method | A command and its owning slice's `Decide` function | Define accepted outcomes and meaningful rejections. |
| Child entity | An immutable value inside the state that retains a domain identity | Decide what identity means and which changes must be atomic. |
| Value object | An immutable domain value used by state and decisions | Enforce valid construction and domain equality. |
| Invariant | A pure rule used by every relevant decision | Keep the rule in one place and apply it on every write path. |
| Repository / unit of work | State loading and atomic commits handled by the runtime host | Select a host that meets the application's persistence needs. |
| Domain event | An internal fact implementing `IDomainEvent` | Name what happened in the language of the model. |
| Public integration contract | A versioned `IIntegrationEvent` produced through a `Publication` | Design the boundary other contexts depend on. |
| Saga / process manager | A declarative `Process` connecting commands and awaited facts | Specify ordering, timeouts, retries, and compensation policy. |
| Query model | A projection built from domain events | Shape the view for the question the user needs answered. |

## A slice is not a new aggregate

Splitting `PlaceOrder` and `CancelOrder` into slices does not split the order's consistency boundary. Both decide against the same keyed `Order` state. A slice is an entry point for an intention, not a reason to give every action a separate copy of the domain model.

Wyrd enforces single ownership of each command. That does not prove that two different commands preserve the same business invariant. If several operations need the same rule, put it on the state, an entity or value object, or a pure domain policy, and have every relevant slice use it. Do not duplicate a credit-limit calculation across five handlers and expect a framework to keep the copies consistent.

The current engine commits one handled envelope's changes atomically through its store. That is not a distributed transaction spanning the complete fulfillment process. Orders, payments, and inventory still own separate rules; a process coordinates their progress.

An event-shaped decision does not force event sourcing: the current host loads serialized state rather than rebuilding it from the full event history for every command. Nor does a separate domain owner force a separate service. The Shop example keeps these owners in a modular monolith.

## Rich models still contain entities and value objects

An order with ten lines is not ten unrelated aggregates merely because its state is immutable. A line can retain a `LineId` while the state contains an immutable collection of lines. Changing a quantity creates a new representation of the same line identity and a new order state. The order's decision checks rules that span those lines.

A `Money` value object still needs a currency, valid amounts, and operations that reject incompatible currencies. A `record` gives you a useful representation, not automatic domain validity. A public constructor that accepts invalid data is still a modeling problem in functional C#.

Those are modeling guidelines, not additional Wyrd APIs. The current Shop sample has one SKU and quantity per order; a full multi-line order and `Money` model are roadmap work. Start from the invariants your domain actually needs rather than copying a proposed rich-order example as a shipped feature.

## Keep the behavior when you remove the mutation

The mistake to avoid is turning an aggregate into a bag of records and moving all its rules into unrelated application services. A functional model separates the representation of state from the operation deciding its next fact; it does not remove the need for cohesive behavior.

For a rule local to an order, keep the policy close to the order model. For a calculation shared by several operations, use a pure domain function. For a fact owned by another bounded context, model how that fact enters your boundary rather than querying a remote service from `Decide`.

In particular, do not call a payment provider, read the wall clock, or fetch inventory over HTTP inside a decider. Those actions make the result depend on an environment that is absent from the function's inputs. External integrations need an explicit execution boundary; the current in-memory host does not supply production adapters for them.

## Separate local consistency from long-running policy

Cancelling one order is a local domain decision. Unwinding an entire checkout may require refunding a payment and releasing stock as well. Those actions belong to their respective owners, connected by a process policy.

Wyrd's `.Undo`, `.Retry`, and `.Within` express that policy. They do not make a refund identical to rolling back a database transaction, or make a non-idempotent provider safe by declaration alone. A compensation can fail. The business must decide what reversal means and how an unresolved outcome will be handled.

Similarly, an `OrderCancelled` domain event is an internal fact. If another bounded context needs to hear about it, define a versioned integration event and map it explicitly. Internal refactoring should not silently redefine someone else's public contract.

## Test a decision as a business statement

The rule can be tested directly without a repository or runtime:

```csharp
using Wyrd.Testing;
using Xunit;

public sealed class CancellationTests
{
    [Fact]
    public void a_cancelled_order_cannot_be_cancelled_again() =>
        Scenario.For(new CancelOrderSlice())
            .Given(new OrderPlaced("A-1"), new OrderCancelled("A-1"))
            .When(new CancelOrder("A-1"))
            .ThenRejected(OrderError.AlreadyCancelled);
}
```

Add specifications for a placed order being cancelled and a missing order being rejected. These are business outcomes, independent of how an application stores the state. Runtime-level scenarios then exercise the separate concerns of delivery, correlation, retries, and timeouts. A pure unit test is not evidence that a production payment adapter works.

## Migrate one boundary at a time

1. Pick one behavior with a clear business outcome and list its invariants. Keep the existing ubiquitous language.
2. Identify the state and key that own those invariants. Do not redesign aggregate boundaries merely to create smaller handlers.
3. Extract the decision from infrastructure. Represent accepted facts and expected errors explicitly, then write business specifications.
4. Route the selected command to one authoritative write path. Do not leave an old handler and a new slice independently updating the same business state.
5. Introduce projections and public-event mappings where the surrounding application needs them. Keep the public contract deliberate.
6. Add process orchestration when several owners must participate over time. Specify failure policy alongside the happy path.

Today, use the in-memory host to learn and validate the model. It loses its data on restart; durable persistence and transport hosts remain planned. Existing production persistence is not replaced simply by removing a repository interface from your code. Resolve the host and operational requirements before routing production traffic through a new model.

## Continue from the example

Continue with [Modelling read models for screens and reports](read-models.md) to shape query views, handle facts from several owners, and test ordering rules without putting business decisions in endpoints.

Read the actual [Shop cancellation slice](https://github.com/aminch18/wyrd/blob/main/samples/Shop/Orders/CancelOrder.cs), [order state](https://github.com/aminch18/wyrd/blob/main/samples/Shop/Orders/PlaceOrder.cs), and [business specifications](https://github.com/aminch18/wyrd/tree/main/samples/Shop.Tests). Then follow the site's messages, processes, contracts, and CLI chapters as implementation references.

For the conventional aggregate and value-object background, see Microsoft's [domain-model guide](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model) and [value-object guide](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/implement-value-objects). Wyrd's functional expression is the project's architectural choice, not a prerequisite of DDD itself.
