# Roadmap

Wyrd's identity is **determinism**: pure deciders + a process model as data, with every side effect owned by the framework.
Releases are named after the Norn they unlock.

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

## v0.1 — Messages done right ([design](design/messages.md))

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
- [ ] AsyncAPI generation

## v0.2 — Skuld (simulate the future)

- [ ] Park awaited events that arrive before the instance reaches the `Await`
- [ ] Steps that read the process state, not only the trigger
- [ ] Typed invokers via source generator (drop `DynamicInvoke` in the pipe syntax)
- [ ] Given/When/Then testing package (`Wyrd.Testing`)
- [ ] **Exhaustive failure simulation**: failures, retries, duplicates, reordering, timeouts
- [ ] First analyzers: exhaustive `[Union]` switch, missing compensation, `Await` without timeout
- [ ] Analyzer: `wyrd_process_style` enforcement with code fix fluent ⇄ pipe

## v0.3 — Urd (replay the past)

- [ ] Process instance history capture
- [ ] `dotnet wyrd replay <instance> --as-test`

## v0.4 — Verdandi (run the present)

- [ ] Integrations (`IIntegration<...>`): the only I/O, with idempotency keys and recorded outcomes
- [ ] Framework reply envelope (Accepted / Rejected / Faulted) and re-send with stable message ids
- [ ] Ports: `IMessageTransport`, `IOutboxStore`, `IProcessStateStore`, `IScheduler`
- [ ] EF Core outbox/inbox/state (PostgreSQL, SQL Server)
- [ ] Durable scheduler for timeouts
- [ ] RabbitMQ transport
- [ ] OpenTelemetry with process-level traces

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
