# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

### Changed

- Quality bar: Q2.3 is checked by the retention tests (in memory and on both EF Core databases), not by a retained-memory
  benchmark. A host's size is the application's data plus what its retention keeps; the tests prove each kind expires
  on time and only then. With Q1 ticked on the roadmap, every rule of the bar is ✅.

### Fixed

- Telecom sample rejects malformed webhook payloads before queuing, returns HTTP 400 for invalid commands, requires sequential payment reviews, validates operator incident ownership, and retries smoke-test connection failures. Provider replies without receipts follow technical retry handling. Local Compose binds the API to loopback; workspace URL decoding and small-text contrast are corrected.

- `EfCoreRuntime.SendAsync` throws `ConcurrencyConflictException` when a conflict outlasts `MaxConflictRetries`, whatever
  the database reported (a stale version, a unique key, a deadlock). Before, the last attempt let the provider's
  exception through, so a caller catching the documented type missed it (Q4.1).

## [0.5.0-rc.1] - 2026-10-24

Verdandi: the durable runtime. A release candidate: the whole quality bar is green except Q2.3's retained-memory
check, which comes before 0.5.0; the public API may still change until then.

### Benchmarks (Q1.4)

The Shop on the in-memory host, 10,000 orders per scenario, Release, .NET 10.0.201, Apple M3 Pro (`--gate`; orders/s
is one run, for information):

| Scenario | KB/order | B/message | Orders/s | 10k/1k |
| --- | ---: | ---: | ---: | ---: |
| SingleSku | 73.0 | 4,400 | 6,225 | 1.003 |
| SkuPerOrder | 79.0 | 4,256 | 10,114 | 0.997 |
| Compensated | 64.1 | 4,376 | 19,002 | 0.993 |
| Timers | 81.0 | 4,149 | 10,818 | 0.983 |

Against 0.4's code base the same gate allocates 5% to 11% more per message (retention, versioned read models,
cancelled timers); `benchmarks/README.md` has the before and after.

### Fixed

- The Shop consumes shipped stock reservations through an independent inventory process, keeping per-SKU state bounded by outstanding reservations. Add business and hostile-future specifications and guidance on state that only grows.

### Breaking

- `Shop.Shipping.ShipOrder` and `Shop.Shipping.OrderShipped` now require `Sku`. Regenerate persisted sample histories created with the previous message shapes before replaying them.

- The tool executable is now `dotnet-wyrd`, so the documented `dotnet wyrd` command works after a global
  installation. Replace direct `wyrd` invocations with `dotnet wyrd` (or `dotnet-wyrd`).
- Failures a host or an application handles throw `WyrdException` subtypes instead of `InvalidOperationException`
  itself (see *Changed*). `catch (InvalidOperationException)` still works; checks for the exact type
  (`ex.GetType() == typeof(InvalidOperationException)`, `Assert.Throws<InvalidOperationException>`) no longer match.
  A command without an owner now says "Nothing owns X. Register its owner with AddSlice() or AddIntegration()."
  instead of "No slice owns X".
- `IWyrdRuntime` has a new member, `Subscriptions`: a class of yours that implements it needs it too.

### Added

- Architecture documentation for reviewers: `docs/design/architecture.md` (the big picture, the model, the patterns,
  the invariants and where they are enforced, how to review a PR, and the discrepancies found between the code and
  the design documents) and `docs/design/runtime.md` (the engine branch by branch, the in-memory and EF Core hosts,
  and one order end to end on PostgreSQL with real logs, SQL and rows).
- Website: a **Running durable** guide (EF Core host, retention, workers, integrations, operators, health, projection
  catch-up and RabbitMQ), the API reference for every Verdandi type, and the roadmap, semantics and decisions pages
  brought up to date with 0.5.
- Website SEO: prerendered HTML and clean URLs for every documentation chapter, unique metadata, canonical and social links, structured data and a generated sitemap; existing hash bookmarks remain supported.
- Document all public library and testing members, enforce CS1591, track each package's API against 0.4.0 with PublicApiAnalyzers, and validate library packages against the 0.4.0 NuGet baseline.
- Ubuntu CI now reports line and branch coverage for `Wyrd` and `Wyrd.Testing`, uploads the full HTML report, and enforces the per-package release floors in `coverage.json`; release floors can only rise.
- Add BenchmarkDotNet Shop scenarios and fast allocation/scaling gates with a measured baseline. Ubuntu enforces every budget and reports the known single-SKU scaling defect until task 016 lands.

- Add BCL `Wyrd` process tracing with envelope context propagation, lifecycle/outcome tags and links for external continuations; Shop.Api exports to the console when `OTEL_EXPORTER=console`. Deterministic journals, domain state and replay histories are unchanged.
- Documentation learning path with the shared read-model guide, working repository-relative links, and a searchable API reference checked against public declarations, analyzer rules and code-fix coverage during the website build.
- `dotnet wyrd mcp`: an official-SDK stdio server exposing the existing Mermaid, AsyncAPI, contract parser and
  Urd replay implementations. Contracts include structured content; failures preserve CLI messages without ending
  the session, and process-discovery diagnostics stay on stderr.
- Add `dotnet wyrd processes --output/--check` with versioned, readable process snapshots and a Shop CI check; structural, added and removed definitions require review, while retry and time-limit changes remain compatible.
- `processes` MCP tool: an agent can snapshot process structure or check it against `processes.wyrd.txt` before
  opening a pull request, with the same output as `dotnet wyrd processes`.
- Native AOT HTTP verification for `Shop.Api`: generated request delegates and JSON metadata, named response
  records, and a reusable `curl`/`jq` smoke script covering checkout, rich drafts, cancellation and published events.
  Ubuntu CI publishes and runs the native API alongside the existing native console walkthrough.
- **Verdandi** (`Wyrd.EntityFrameworkCore`, in progress): the durable runtime on PostgreSQL and SQL Server.
  `EfCoreRuntime<TContext>` handles every message in one transaction that also takes it off the queue — state,
  events, instances, inbox, outbox, timers and read models commit together or not at all (an integration's outside
  call is made before that transaction, under a lease: see *Integrations*). The queue is a table shared
  by any number of workers (`FOR UPDATE SKIP LOCKED` / `READPAST`, the only raw SQL); failures are redelivered with
  backoff and poison messages go to dead letters; processes survive restarts. `modelBuilder.AddWyrd()` puts Wyrd's
  tables in your own `DbContext` and migrations, and `SendAsync(command, context)` joins your transaction. JIT only:
  EF Core's Native AOT support is experimental.
- **Integrations** (`Integration<TCommand, TEvent, TError>`, `AddIntegration`): the only I/O. An integration owns a
  command like a slice; its event is recorded and published like a domain event, its error is a business rejection,
  and an exception is a transient failure for the process's retry policy. `IntegrationContext.IdempotencyKey` is the
  command's message id, stable across retries and redeliveries. On the EF Core host the call is made with no
  transaction open: the command is leased (`EfCoreRuntimeOptions.IntegrationLease`, five minutes), and its outcome is
  recorded in a second transaction, also after the lease ran out (the first outcome recorded wins). A third party's
  timeout is a failure for the retry policy, not a cancellation. A step owned by an integration that never answered
  is undone on compensation in case it acted, instead of being taken as never run (its undo treats "nothing to undo"
  as done). Design: `docs/design/integrations.md`.
- Operators on every host: `IWyrdRuntime.ResumeAsync` / `CompensateAsync`.
- One retry layer on the durable host: a process command it can't handle goes back to its process as a failure (the
  step's retry policy decides, then the instance is suspended); dead letters are only for messages no process decides
  on (events, replies, timers).
- **Transport between services** (core): `IWyrdTransport` carries published integration events, each with a stable
  message id (`TransportMessage`); `IWyrdRuntime.ReceiveAsync` delivers one from another service, ignores contracts
  the service doesn't use and handles a redelivery once. Only integration events cross service boundaries: commands
  have one owner inside one service. `InMemoryTransport` connects in-memory runtimes (`UseTransport`, `Subscribe`);
  the durable relay comes with the EF Core host. Traces cross with the message (`TraceParent`, `TraceState`), and an
  integration's call to the outside world is a client span under its command.
- **Retention** (Q2.3, semantics 4.1): everything a host keeps has one, and on a durable host it is a decision written
  in the application: `EfCoreRuntime` requires a `Retention` whose every member is `Keep.For(period)` or
  `Keep.Forever("why")` — deduplication, finished instances and their histories, relayed messages, domain events, dead
  letters. Workers remove what expired in batches every `CleanupInterval` (`ForgetExpiredAsync`). Suspended instances
  never expire; a removed finished instance frees its key. Fences never expire either, and a reply that reaches an
  instance whose definition changed is kept on the instance for the operator, so neither depends on the deduplication
  window. In memory: `UseRetention` (keeps everything by default) and a bounded journal (`JournalLimit`, 10,000).
- **Catching up a projection** added to a store that already has data: `Projection<TView>.Seed<TState>(key, seed)`
  builds the view from the current states (Wyrd keeps state, not every past event), once, and the projection follows
  their events after that; an event already in a seed is skipped, a later one applied. Hosts catch up on their own
  (workers on EF Core, the in-memory runtime when idle) or on `CatchUpAsync()`. Projected versions are now kept even
  in applications without projections.
- **`Wyrd.RabbitMq`**: RabbitMQ between services, kept thin. `RabbitMqTransport` (give it to `UseTransport`) publishes
  each integration event to a durable topic exchange with its contract as the routing key, persistent, with its
  message id and trace context, and counts it sent once the broker confirms: an unconfirmed message stays in the
  outbox. `RabbitMqSubscriber.StartAsync(connection, runtime, queue)` binds the service's durable queue to the
  contracts the runtime uses and acknowledges a message once the runtime has it; the runtime handles a redelivery
  once. `IWyrdRuntime.Subscriptions` lists those contracts (processes, projections, upcasters; not what the service
  only publishes).
- `IWyrdBuilder<T>`: register a module the same way on any host (`ShopModule.Register(runtime)`).
- `IWyrdRuntime`: what application code needs (send, publish, query), implemented by both hosts.
- `samples/Shop.Api` runs durable on PostgreSQL when built with `-p:WyrdHost=EfCore` and given a `Wyrd` connection
  string (`compose.yaml` included; its `ShopDbContext` carries Wyrd's tables and migrations). The default build stays
  in memory and Native AOT.
- Ordered read models and instance histories on the durable host; `IWyrdRuntime.HistoryOfAsync` on both hosts, so an
  instance recorded in the database replays locally with Urd.
- **A hardened queue** on the EF Core host: idle workers back off from `PollInterval` to `MaxPollInterval` (2 s), and a
  message the runtime queues itself wakes them at once. `HealthAsync` returns a `QueueHealth` (due messages and the
  oldest one's age, scheduled messages, dead letters, pending steps and the oldest one's age, suspended instances);
  workers publish it every `HealthInterval` on the `Wyrd` meter, with counters of handled, failed and dead-lettered
  messages. Instances carry `pending_since`; `EfCoreRuntime` is `IDisposable`.
- **History archive** (`IHistoryArchive`, `UseHistoryArchive` on both hosts): before retention removes an expired
  finished instance, its history is handed to the application (blob storage, a cold table...); it is removed only once
  the archive returned, and a failed archive keeps it for the next pass. An archived history still replays with Urd.

### Changed

- Failures a host or an application handles are typed and derive from `WyrdException` (itself an
  `InvalidOperationException`, so existing `catch` blocks still work): `ConcurrencyConflictException`,
  `UnknownMessageTypeException`, `MissingJsonMetadataException`, `MissingUpcasterException`,
  `NotAnIntegrationEventException`, `InstanceStateException` (Q4.1). A conflict in your own transaction
  (`EfCoreRuntime.SendAsync(command, context)`) is a `ConcurrencyConflictException` to retry.
- A wait's timer is cancelled when the wait ends (the awaited event arrives, or the instance is suspended while
  waiting), instead of firing later and being ignored: nothing stays scheduled for an instance that no longer waits
  (Q2.2). The EF Core queue gains a `message_id` column for it.
- In-memory reads (`View`, `StatusOf`, `Journal`...) no longer wait for an operation in progress: they see the last
  commit, behind a short lock instead of a blocking wait on the runtime's gate (Q4.2). The threading analyzers guard
  every project under `src/` against sync-over-async.
- Urd compares entries in order without their time: on a real clock a reply takes milliseconds, on replay none.

## [0.4.0] - 2026-10-18

### Added

- **Instance history (Urd)**: every process instance records what crossed its boundary — trigger, commands sent,
  replies, awaited events, timeouts, suspensions, operator actions — committed with the instance itself.
  `InMemoryRuntime.HistoryOf(process, key)` returns it; `ToJson()` / `InstanceHistory.FromJson(...)` export it.
  Design: `docs/design/urd.md`.
- **Urd** (`Wyrd.Testing`): `Urd.ReplayAsync(system, process, history)` replays a recorded instance against today's
  code — the process runs for real, each command is answered with the recorded reply, the clock replays retries and
  timeouts — and reports the first entry that differs (`ReplayReport.Divergence`, `ThrowIfDiverged()`). A history
  recorded with another process structure is reported up front.
- `dotnet wyrd replay <history.json>` prints an instance's timeline; with `--as-test --runtime ... --process ...`
  it writes an xUnit test that embeds the history and replays it with Urd.
- Rich Shop order aggregate: immutable identified lines, decimal `Money`, shared state-owned rules, editable drafts,
  line and draft cancellation, rich detail projection and HTTP examples. Submission freezes the draft for the
  existing single-SKU EUR fulfillment; the original `PlaceOrder` shortcut remains supported.

## [0.3.0] - 2026-10-18

### Added

- IDE **Fix all** in document, project or solution for contract snapshots (WYRD0200/WYRD0202) and JSON
  declarations (WYRD0300/WYRD0301/WYRD0302): coordinated edits preserve existing attributes and snapshot comments,
  avoid duplicate declarations, and resolve JSON name collisions exposed by newly declared union cases.
- **Skuld shrinking**: broken futures replay with fewer perturbations while preserving the violated invariant.
  `Violation.Shrunk` contains the reduced perturbations, detail and journal; reports show it first and retain the
  original seed. Shrinking defaults on with at most 200 replays per broken future (`Shrink`, `ShrinkBudget`).

- **Skuld** (`Wyrd.Testing`): `Skuld.SimulateAsync(system, scenario, invariants, options)` runs a scenario in many
  seeded futures — transient faults, redelivery, reordering, timers — checks invariants (`Invariant.ProcessesSettle`,
  `Invariant.NothingNeedsAttention`, your own) and reports each broken future with its perturbations, journal and
  seed; `options.Only(seed)` replays it exactly. Misconfigured simulations are rejected.
- Process analyzers, in fluent and pipe syntax: WYRD0014 (a step must say how it retries: `.Retry(...)` or
  `.NoRetry("why")`, there is no default), WYRD0011 (a step followed by others must say how it is undone:
  `.Undo(...)` or `.NoCompensation("why")`) and WYRD0012 (`Await` without `.Within(...)`).
- WYRD0013: `wyrd_process_style = fluent | pipe | any` in `.editorconfig` picks a repository's process syntax; a
  code fix converts a process from one syntax to the other.
- Exhaustive switches over `[Union]` states: WYRD0020 names the cases a switch misses, and a diagnostic suppressor
  removes CS8509 when every case is handled, so `_` is no longer needed and a new case breaks the build.
- `.NoRetry("why")`, `.NoCompensation("why")`, `.Undo(command, 20.Times)` and `.OnCompensated(command).Retry(...)`.
- **Suspension**: when Wyrd can't resolve something — a step's retries exhausted, an undo or the outcome that never
  succeeds — the instance is `Suspended` with a reason (`ReasonOf`) instead of guessing. `ResumeAsync` re-sends what it
  was doing with the same message id; `CompensateAsync` undoes it and fences the step it gave up on; a late success
  resumes it on its own. `.Retry(3.Times).OrCompensate()` opts into compensating automatically.
- **Process fingerprint**: each instance remembers the structure of the process it started with
  (`Process.Fingerprint`: steps, undos, waits, outcome — not retries or time limits). If a redeploy changes it, the
  instance is suspended ("definition changed") instead of continuing at the wrong step; nothing it receives meanwhile
  is lost, and it resumes once its definition is back.
- A suspended instance keeps events it awaits (parked) instead of dropping them.
- **Steps read earlier events**: a step (or an undo) may take, after the trigger, events earlier steps produced or the
  process awaited: `.Then(Shipping.Ship)` with `ShipOrder Ship(OrderPlaced order, PaymentCharged charged)`. A step
  reading an event no earlier step produces fails before the first message; an undo reads only events from before
  its own step.
- **Ordered read models per stream**: domain events carry their stream (state and key) and version; a projection
  gets the events of one stream in the order they happened, holding an early one until the gap is filled. Processes
  don't wait. Across streams, and for integration events from outside, there is still no order.
- `Wyrd.Tool` (`dotnet wyrd`): AsyncAPI 3.0 documentation from contract snapshots and Mermaid diagrams of processes.
- `Wyrd.Web`: the documentation site, with the philosophy and the classic-DDD-to-Wyrd guides.
- `docs/design/semantics.md`: the specification of how Wyrd behaves.
- `CODE_OF_CONDUCT.md`, launch material under `docs/launch/`, and symbol packages (`.snupkg`) next to every package.

### Changed

- **Breaking:** `ProcessStatus.Failed` is now `ProcessStatus.Suspended`, and exhausted retries suspend instead of
  compensating (opt back in per step with `.OrCompensate()`).
- **Undos always insist.** An undo no longer borrows its step's retry policy: undoing, and the outcome command,
  retry 10 times with exponential backoff by default (waits capped at a minute), adjustable, never off. Whether a
  step retries is its own explicit decision.
- Pipe-syntax steps no longer use reflection to run: they are bound to typed calls (delegate variance) when the stage
  is added, so a step may also take a more general type than the trigger.
- CI builds and tests on Linux, Windows and macOS; the release job can push packages to nuget.org.
- README opens with what Wyrd is, an honest preview status and a two-minute way to try it.

### Fixed

- A redelivered failure reply no longer consumes an extra retry: replies carry their attempt; late successes are
  accepted.
- A step given up on without knowing whether it ran is fenced by its undo: the owner atomically blocks the original
  command if it never ran (late copies never execute) or undoes it if it did. A domain rejection of an undo is no
  longer taken as proof that there was nothing to undo.
- The message type registry is thread-safe (scenarios in parallel test classes could corrupt it).

### Samples

- The shop states how every step retries, releases only what an order reserved, handles every union case without
  `_`, and its orders read model only moves statuses forward — all found by Skuld or the new analyzers.

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

[Unreleased]: https://github.com/aminch18/wyrd/compare/v0.5.0-rc.1...HEAD
[0.5.0-rc.1]: https://github.com/aminch18/wyrd/compare/v0.4.0...v0.5.0-rc.1
[0.4.0]: https://github.com/aminch18/wyrd/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/aminch18/wyrd/compare/v0.2.0-alpha.1...v0.3.0
[0.2.0-alpha.1]: https://github.com/aminch18/wyrd/compare/v0.1.0-alpha.1...v0.2.0-alpha.1
[0.1.0-alpha.1]: https://github.com/aminch18/wyrd/releases/tag/v0.1.0-alpha.1
