# Roadmap

Wyrd's identity is **determinism**: pure deciders + a process model as data, with every side effect owned by the framework.
Milestones map to SemVer minor versions (v0.1 → `0.1.0`); previews are tagged `-alpha.n`. See [RELEASING.md](RELEASING.md).

## ✅ v0.0 — Spike

- [x] Process model as data (`Process`, `StepNode`, `AwaitNode`) with structural equality
- [x] Fluent syntax (default) and opt-in pipe syntax (`>>` via C# 14 extension operators), building the same model
- [x] C# 14 extension member literals: `15.Minutes`, `3.Times`
- [x] `Result<TOk, TError>` with implicit conversions
- [x] `[Union]` marker for closed hierarchies
- [x] Mermaid rendering (living docs)

## ✅ v0.0.x — Runnable core

- [x] Slices as commands with a static pure `Decide` (`ICommand<TSelf, TState, TEvent, TError>`), state via `IState<TSelf>`
- [x] Steps as pure functions `trigger -> command`, type-checked in the fluent syntax, validated at build in the pipe syntax
- [x] Explicit correlation: `StartOn<T>(e => e.Key)`, `Await<T>(e => e.Key)`
- [x] Deterministic `InMemoryRuntime` with a virtual clock: retries, reverse compensation, timeouts, duplicates, correlation
- [x] `samples/Shop`: four vertical slices, six scenarios
- [x] Library scaffolding: central package management, MinVer, SourceLink, CI (3 OSes), templates

## ✅ v0.1 — Messages done right ([design](design/messages.md)) · `0.1.0-alpha.1`

- [x] Message taxonomy: `ICommand`, `IDomainEvent`, `IIntegrationEvent` + `[IntegrationEvent(name, Version)]`
- [x] Contracts split from slices: `Slice<TCommand, TState, TEvent, TError>`
- [x] Explicit publication mapping (`Publication<TDomainEvent, TIntegrationEvent>`)
- [x] Runtime rules: one owner per command, only integration events published from outside
- [x] Analyzers: command vs event (WYRD0001), no domain types in contracts (WYRD0101), complete contracts (WYRD0102)
- [x] Contract snapshots (`contracts.wyrd.txt`) + breaking-change detection (WYRD0200–0204), shipped in the package
- [x] Multi-version publishing (several publications per domain event)
- [x] Code fix: write the suggested line into `contracts.wyrd.txt` (adds, replaces in place, or creates the file)
- [x] Upcasters for subscribers: each subscriber gets a fact once, in its version (chained v1 -> v2 -> v3)
- [x] Analyzer: one owner per command at compile time (WYRD0002)
- [x] Analyzer: naming suggestions, imperative commands and past-tense events (WYRD0003, WYRD0004)

## ✅ v0.2 — Distributed-shaped core, in memory · `0.2.0-alpha.1`

- [x] Message-driven engine: steps are command messages, owners answer with replies (Accepted / Rejected / Faulted)
- [x] Everything crosses the queue as JSON envelopes; states are stored as JSON
- [x] One transaction per message: state, events, instances, inbox, outbox and timers commit together
- [x] Storage port (`IWyrdStore`) with a deterministic in-memory implementation
- [x] Retries scheduled with backoff on the clock, re-sent with the same message id; inbox replays stored outcomes
- [x] Early events parked until the process reaches the matching `Await`
- [x] Deterministic message ids
- [x] Projections (read models) with redelivery protection
- [x] Thread-safe in-memory host; `samples/Shop.Api` HTTP sample
- [x] `Wyrd.Testing`: Given/When/Then for slices and projections, with JSON round-trip checks; `samples/Shop.Tests`
- [x] Process outcomes as commands (`.OnCompensated(Orders.Cancel)`), so read models learn how a process ended
- [x] Source-generated JSON: `[WyrdJsonContext]`, named types (no `Type.GetType`), polymorphic unions,
      analyzers WYRD0300/WYRD0301 with code fixes; reflection mode for development
- [x] Native AOT: the shop sample publishes and runs as a native executable, checked in CI
- [x] Analyzer WYRD0302: types sharing a name in the JSON context (union cases, namespaces, nested property types) get a unique `TypeInfoPropertyName` via code fix, before SYSLIB1031

## v0.3 — Skuld (simulate the future)

- [x] Retry semantics ([design](design/semantics.md) 3.2–3.4): every step declares `.Retry(...)` or `.NoRetry("why")`
      (WYRD0014, no default); undos and the outcome always insist with their own adjustable policy; WYRD0010 removed
- [x] Suspend instead of guessing ([design](design/semantics.md) 3.6): exhausted retries, exhausted undos and a
      failed outcome suspend the instance with a reason; `ResumeAsync` / `CompensateAsync`; opt-in `.OrCompensate()`
- [x] Process structure fingerprint per instance ([design](design/semantics.md) 3.1): a changed definition suspends in-flight instances
- [x] Steps that read the events of earlier steps (and awaited events), not only the trigger; checked before the first message
- [x] Typed invokers in the pipe syntax: plain delegates are bound to typed calls through delegate variance when the
      stage is added; only steps that read earlier events are invoked dynamically (Native AOT-compatible)
- [x] **Failure simulation** (`Skuld.SimulateAsync`): seeded futures with transient faults, redelivery and reordering,
      timers on the clock, invariants (`Invariant.Defaults` + your own), replay by seed, actionable reports
- [x] Engine fixes found by Skuld: attempt-aware replies (stale faults ignored), unknown-outcome steps compensated,
      `.OnCompensated(...).Retry(...)`, thread-safe type registry
- [x] Ordered projections per stream (events carry their stream version), so read models don't fight reordering by hand
- [x] Shrinking: reduce a failing future to the fewest perturbations that still break the invariant
- [x] Analyzers WYRD0011 (a step must say how it is undone: `.Undo(...)` or `.NoCompensation("why")`) and
      WYRD0012 (`Await` without `.Within(...)`), in fluent and pipe syntax
- [x] Exhaustive `switch` over a `[Union]`: WYRD0020 names missing cases; a suppressor removes CS8509 when every case
      is handled, so no `_` is needed and new cases break the build
- [x] Analyzer WYRD0013: `wyrd_process_style` (fluent | pipe | any) in `.editorconfig`, with a code fix that converts
      a process between fluent and pipe syntax, keeping every argument as written

## v0.4 — Urd (replay the past) and the `dotnet wyrd` tool

- [ ] Process instance history capture
- [ ] `dotnet wyrd replay <instance> --as-test`
- [x] `dotnet wyrd asyncapi`: AsyncAPI document from the integration event contracts (moved from v0.1)
- [x] `dotnet wyrd diagram`: Mermaid diagrams for every process

## v0.5 — Verdandi (run the present)

- [ ] Integrations (`IIntegration<...>`): the only I/O, with idempotency keys and recorded outcomes
- [ ] Framework reply envelope (Accepted / Rejected / Faulted) and re-send with stable message ids
- [ ] Ports: `IMessageTransport`, `IOutboxStore`, `IProcessStateStore`, `IScheduler`
- [ ] EF Core outbox/inbox/state (PostgreSQL, SQL Server)
- [ ] Durable scheduler for timeouts
- [ ] RabbitMQ transport
- [ ] OpenTelemetry with process-level traces

## Documentation & adoption (continuous, required before going public)

Wyrd is a paradigm shift for most .NET teams: functional DDD instead of mutable aggregates. Teaching the
mental model matters as much as the code.

- [x] Guide: **[From classic DDD to Wyrd](guides/from-ddd-to-wyrd.md)** — aggregate root, entities, value objects, invariants, repositories
      and domain events mapped one by one, with the same example written both ways
- [x] Rich aggregate in `samples/Shop`: `Order` with `OrderLine` entities and a `Money` value object;
      `AddLine`, `CancelLine` and `CancelOrder` slices sharing invariants defined on the state; API endpoints
- [x] Guidance: shared invariants live on the state or in pure domain policies, never duplicated across slices
      (covered in the DDD guide; an analyzer for state-module cohesion is still a possible future addition)
- [x] Guide: modelling read models (projections) for screens and reports
- [ ] Docs site with the mental model first, API reference second

## v1.0 — Production ready

- [ ] **Process versioning**: snapshot of every process; breaking changes fail the build until in-flight instances are migrated
- [ ] Native AOT verified end to end
- [ ] `dotnet wyrd` CLI + MCP server for agents
- [ ] Docs site

## Later

- Event Modeling ⇄ code
- Live process dashboard
- Azure Service Bus, Kafka transports; community adapters (Wolverine, MassTransit)

## Open decisions

- State persistence: state-stored, event-sourced, or both
- The full rules catalog (what "correct" means, rule by rule)
