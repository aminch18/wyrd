# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Process analyzers, in fluent and pipe syntax: WYRD0010 (a step with `.Undo(...)` but no `.Retry(...)`), WYRD0011
  (a step followed by others must say how it is undone) and WYRD0012 (`Await` without `.Within(...)`).
- `.NoCompensation("why")`: declares that a step needs no undo, explicitly.

### Changed

- Pipe-syntax steps no longer use reflection to run: they are bound to typed calls (delegate variance) when the stage
  is added, so a step may also take a more general type than the trigger.

- `Wyrd.Tool`: generate AsyncAPI 3.0 documentation from reviewed contract snapshots and Mermaid diagrams from compiled processes, with Spectre CLI help and validation, typed YAML serialization, and nullable/immutable collection mapping.
- `Wyrd.Web`: responsive marketing and framework documentation site with light/dark themes, IDE-matched code palettes, and automated GitHub Pages deployment.
- Shared Markdown guides for the Wyrd philosophy and the transition from classic DDD, with equivalent cancellation examples, a concept map, and adoption guidance.
- **Skuld** (`Wyrd.Testing`): `Skuld.SimulateAsync(system, scenario, invariants, options)` runs a scenario in many
  seeded futures — transient faults, redelivery, reordering, timers — checks invariants (`Invariant.ProcessesSettle`,
  `Invariant.NothingNeedsAttention`, your own) and reports each broken future with its perturbations, journal and seed;
  `options.Only(seed)` replays it exactly.
- `.OnCompensated(command).Retry(...)`: the outcome command can retry.

- `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1) and launch material under `docs/launch/` (demo script, post drafts, checklist).
- Symbol packages (`.snupkg`) are produced next to every package, and the release job pushes packages to nuget.org when the `NUGET_API_KEY` secret exists.

### Changed

- CI builds and tests on Linux, Windows and macOS now that the repository is public.
- README opens with what Wyrd is, an honest preview status and a two-minute way to try it.
- Scope website styles with CSS Modules, enforce readable formatting, use Shiki for C# and shell highlighting, and document the Wyrd CLI's AsyncAPI and Mermaid commands.

### Fixed

- A redelivered failure reply no longer consumes an extra retry: replies carry their attempt; late successes are accepted.
- A step given up on without knowing whether it ran is fenced by its undo: the owner atomically blocks the original
  command if it never ran (late copies never execute) or undoes it if it did. A domain rejection of an undo is no
  longer taken as proof that there was nothing to undo.
- Skuld rejects misconfigured simulations (no runs, rates outside [0, 1]) instead of passing without exploring.
- The message type registry is thread-safe (scenarios in parallel test classes could corrupt it).

### Changed (samples)

- The shop retries every step that can be undone, releases only what an order reserved, and its orders read model
  only moves statuses forward. All found by Skuld.

## [0.2.0-alpha.1] - 2026-10-16

The in-memory runtime now behaves like the distributed one, with read models, business-readable tests,
process outcomes, and source-generated JSON verified under Native AOT.

### Changed

- **Breaking:** the runtime is asynchronous: `SendAsync`, `PublishAsync`, `AdvanceAsync`.
- **Breaking:** states, events and views are serialized as their declared type; unions carry a `$type` case discriminator.
- The in-memory runtime now behaves like the distributed one: a message-driven engine where process steps are
  command messages answered by replies, JSON envelopes, one transaction per message (state, events, instances,
  inbox, outbox, timers), retries scheduled with backoff and re-sent with the same message id, deterministic ids.

### Added

- Awaited events that arrive before the process reaches the `Await` are parked and used when it gets there.
- Storage port (`IWyrdStore`) with a deterministic in-memory implementation.
- `Projection<TView>`: read models kept up to date from events with pure functions, stored in the same transaction;
  `View<TView>(key)` and `Views<TView>()` to query them. Redelivered messages are never applied twice.
- Source-generated JSON: `new InMemoryRuntime(MyJson.Default)` with a `[WyrdJsonContext]` is reflection-free and
  Native AOT ready; analyzers WYRD0300 (type missing from the context) and WYRD0301 (union case not declared as a
  derived type) with code fixes. Types are resolved by registered name; unions are stored polymorphically.
- Analyzer WYRD0302: two types with the same name in the JSON context (union cases like `Order.None`/`Payment.None`,
  same-named types in different namespaces, or types nested in messages) are reported with a code fix that adds a
  unique `TypeInfoPropertyName`, instead of the generator's generic SYSLIB1031.
- `.OnCompensated(command)`: how a process ends when it is compensated (e.g. `CancelOrder`), so read models and
  slices learn the outcome; runs even when there was nothing to undo.
- `Wyrd.Testing` package: `Scenario.For(slice).Given(...).When(...).Then(...)/ThenRejected(...)/ThenState(...)` and
  `Scenario.For(projection).Given(...).Then(key, view)`; every expectation checks the JSON round trip.
- `samples/Shop.Tests`: the shop's business specifications.
- `InMemoryRuntime` is safe to share across concurrent requests (one message at a time, like a queue consumer).
- `samples/Shop.Api`: the shop as a minimal HTTP API with a `.http` walkthrough; the console sample moves to
  `samples/Shop.Console` and the domain becomes the `samples/Shop` library.

## [0.1.0-alpha.1] - 2026-10-16

First preview: the core model, a deterministic in-memory runtime and versioned integration contracts.

### Added

- Message taxonomy: `ICommand`, `IDomainEvent`, `IIntegrationEvent` and `[IntegrationEvent(name, Version)]`.
- `Slice<TCommand, TState, TEvent, TError>`: the single owner of a command, separate from the command contract.
- `Publication<TDomainEvent, TIntegrationEvent>`: the explicit boundary from internal facts to public contracts;
  several publications per domain event allow publishing two contract versions during a migration.
- Runtime rules: one owner per command, only integration events can be published from outside,
  integration events exposed separately from domain events (`Published`).
- `Wyrd.Analyzers`, shipped inside the package: message rules (WYRD0001, WYRD0101, WYRD0102) and integration
  event contract snapshots in `contracts.wyrd.txt` with breaking-change detection (WYRD0200–WYRD0204).
- `Upcaster<TFrom, TTo>`: subscribers receive integration events in the contract version they understand, upcast
  step by step; during dual publishing each subscriber gets the fact once.
- IDE code fix for WYRD0200/WYRD0202: writes the suggested line into `contracts.wyrd.txt`.
- Analyzers: one slice per command (WYRD0002), naming suggestions for events and commands (WYRD0003, WYRD0004).

- Process model as data with structural equality and Mermaid rendering.
- Fluent syntax and opt-in pipe syntax (`>>`) building the same process.
- Deterministic `InMemoryRuntime` with a virtual clock: retries for transient faults, compensation in reverse order,
  timeouts, duplicate detection and correlation.
- `samples/Shop`: an order fulfillment process across four vertical slices.

[Unreleased]: https://github.com/aminch18/wyrd/compare/v0.2.0-alpha.1...HEAD
[0.2.0-alpha.1]: https://github.com/aminch18/wyrd/compare/v0.1.0-alpha.1...v0.2.0-alpha.1
[0.1.0-alpha.1]: https://github.com/aminch18/wyrd/releases/tag/v0.1.0-alpha.1
