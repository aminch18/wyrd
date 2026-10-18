# The Wyrd philosophy

## Put the business back in the foreground

A conversation about an order sounds simple: reserve stock, charge the payment, wait for confirmation, then ship. If payment is declined, release the stock. If confirmation never arrives, refund the payment. These are business policies, not implementation details.

Yet the code that implements them often spreads across handlers, database transactions, message consumers, retry middleware, and scheduled jobs. Each part can be locally reasonable while the complete process is hard to see. Reviewing the business means reconstructing what all those pieces will do together.

Wyrd's ambition is to make that conversation visible in the code. The domain describes what is allowed to happen. A process describes what should happen next. The runtime handles the mechanics of carrying those decisions out.

The payoff is not fewer lines at any cost. It is a smaller gap between what the team understands about the business and what a reviewer can verify in the implementation.

## Keep DDD. Change how behavior is expressed

DDD gives us a language for the business, boundaries between models, and consistency rules that must hold inside those boundaries. None of that depends on mutating an object or loading it through a repository interface.

In a conventional object-oriented model, a method checks the current aggregate and changes its fields. In Wyrd, a slice receives a command and an immutable state. It returns either a domain event describing the accepted change or a domain error explaining the rejection. The state evolves by applying the accepted fact.

```text
Command + current state → domain event or rejection
Current state + domain event → next state
```

Behavior has not disappeared into a generic CRUD service. It is explicit in the decision and the state transition. Shared rules can still live on state records, entities, value objects, or pure domain functions. A record is not a rich model merely because it is immutable; it becomes one when the business rules are expressed and consistently applied.

This separation lets us ask a precise question: given these facts, should this command succeed? Answering it does not require a database, a message broker, or a mock payment gateway.

## Draw a deliberate boundary around effects

The decision to charge a payment and a network request to a payment provider are different responsibilities. A pure decision cannot know whether a remote call timed out before or after the provider accepted it. Moving that uncertainty into a decider makes its result depend on more than its inputs.

Wyrd keeps user-facing deciders and process steps pure. They return facts or intentions instead of publishing, persisting, or scheduling directly. The runtime owns message handling, state persistence, deduplication, declared retries, timers, and process progress. Business authors still choose the policy: which steps may retry, how long a wait lasts, and what compensates a completed action.

This is an architectural direction as well as an implementation boundary. The current host is in memory. Durable storage, transports, and the integration APIs for real external I/O are roadmap work. A sample payment decision is not a production payment-provider adapter.

## Functional C# without a new language

The building blocks are ordinary records, methods, pattern matching, and explicit result values. The important discipline is that a decision gets the information it needs through its inputs and returns its answer. You do not need a separate functional language to discuss or test that rule with another .NET developer.

Using events in that model does not require event sourcing or microservices. The current runtime stores serialized state and events; it does not reconstruct every state by replaying its complete history on each command. The Shop sample is a modular monolith, with several domain owners in one application. Model boundaries and deployment boundaries are different choices.

## Make the process part of the model

An order, a payment, and a stock reservation do not become one aggregate because they participate in the same checkout. They own different rules and can succeed or fail independently. The business still needs a policy connecting them.

A Wyrd process expresses that policy as immutable data: start on this event, send these commands, await this fact, and compensate these completed steps if the flow cannot finish. The fluent and pipe syntaxes describe the same process model.

Because the process is data, it can be inspected and rendered as a diagram today. Deterministic simulation and recorded-instance replay build on the same idea, but remain planned in the current documented version. The promise is one model serving several tools, rather than a diagram maintained separately from the running behavior.

Compensation is a business action, not time travel. A refund does not erase the charge that happened. An email cannot be unsent. Choosing a meaningful undo policy remains domain work; the framework provides the execution mechanics around the policy you declare.

## Use determinism to make behavior explainable

For pure decisions, the same command and state produce the same result. In the in-memory runtime, time advances through an explicit virtual clock. A timeout test can advance fifteen minutes without sleeping, and a process journal exposes the sequence of decisions and transitions.

That makes failures concrete. Instead of asking whether a handler works in isolation, a reviewer can ask what happens if confirmation arrives early, the trigger is duplicated, or a declared retry is exhausted. Wyrd already tests those runtime behaviors; exploring combinations automatically with Skuld is a separate roadmap capability.

Determinism does not prove that a rule is right. A consistently wrong tax calculation is still wrong. Its value is that a decision can be reproduced, challenged, and changed without first recreating an accidental environment.

## Keep internal facts separate from external promises

A domain event says what happened inside a model. An integration event is a public contract offered to other models. Making those the same type couples an internal refactoring to every consumer.

Wyrd uses an explicit publication mapping between them. Reviewed contract snapshots and Roslyn diagnostics make changes to public event shapes visible during development. This checks structural compatibility; it cannot detect a change in what a field means to the business. That still belongs in the conversation and the review.

## Build for people who understand the product

The intended reader is a .NET developer who needs to reason about the business, not become an expert in every failure mode of a distributed system before changing a rule. Small pure decisions, named errors, explicit contracts, and readable process policies make that work more approachable. They also give automated tools a smaller surface to generate and inspect.

Wyrd does not choose your bounded contexts, discover your invariants, or turn an arbitrary workflow into a safe one. It aims to give those decisions a clear place to live and a framework that can increasingly check the mechanics around them.

## Choose it for the problem you actually have

Wyrd is worth exploring when business rules, message boundaries, and long-running processes are the difficult part of the application. It is early-alpha software, and the current runtime is not durable across restarts. A production requirement for a durable broker-backed host is not satisfied by the in-memory sample.

For a simple CRUD application, the extra command/event/state model may add more ceremony than value. Adoption can begin with one meaningful workflow or one bounded context; a rewrite of the entire application is not the goal.

If you come from an aggregate-and-repository model, continue with [From classic DDD to Wyrd](from-ddd-to-wyrd.md). It follows the same cancellation rule through both implementations and maps the familiar concepts one by one.
