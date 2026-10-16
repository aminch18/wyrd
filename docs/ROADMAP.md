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

## v0.1 — Skuld (simulate the future)

- [ ] Slice model: `Decide(command, state)` / `Evolve(state, event)`, discovered by a source generator
- [ ] Step and compensation signatures (decide what a step receives and returns)
- [ ] In-memory runtime that executes processes deterministically
- [ ] Given/When/Then testing package (`Wyrd.Testing`)
- [ ] **Exhaustive failure simulation**: failures, retries, duplicates, reordering, timeouts
- [ ] First analyzers: exhaustive `[Union]` switch, missing compensation, `Await` without timeout
- [ ] Analyzer: `wyrd_process_style` enforcement with code fix fluent ⇄ pipe

## v0.2 — Urd (replay the past)

- [ ] Process instance history capture
- [ ] `dotnet wyrd replay <instance> --as-test`

## v0.3 — Verdandi (run the present)

- [ ] Ports: `IMessageTransport`, `IOutboxStore`, `IProcessStateStore`, `IScheduler`
- [ ] EF Core outbox/inbox/state (PostgreSQL, SQL Server)
- [ ] Durable scheduler for timeouts
- [ ] RabbitMQ transport
- [ ] OpenTelemetry with process-level traces

## v1.0 — Production ready

- [ ] **Process versioning**: snapshot of every process; breaking changes fail the build until in-flight instances are migrated
- [ ] Integration event contract snapshots + upcasters
- [ ] Native AOT verified end to end
- [ ] `dotnet wyrd` CLI + MCP server for agents
- [ ] Docs site

## Later

- Event Modeling ⇄ code
- Live process dashboard
- Azure Service Bus, Kafka transports; community adapters (Wolverine, MassTransit)

## Open decisions

- License (MIT vs Apache-2.0)
- State persistence: state-stored, event-sourced, or both
- The full rules catalog (what "correct" means, rule by rule)
