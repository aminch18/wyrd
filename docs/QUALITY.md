# Quality bar

> Status: **adopted 2026-10-18**. This is the release gate for **every minor from 0.5 to 0.9, and for 1.0**.
> [semantics.md](design/semantics.md) says what Wyrd does; this document says how good it must be. If the code and
> this document disagree, fix the code, or change the document on purpose in the same PR.

Wyrd asks teams to trust it with their business processes. That trust needs numbers, not adjectives: how much a
message costs, how memory behaves after a week, what counts as a breaking change, how failures surface. Each rule
below has an id, a check, and a marker:

- ✅ **enforced**: the rule holds today and its check (the *Check* column: CI, or review where it says so) is in place;
  a red CI check blocks the merge;
- 🔜 **not yet enforced**: new code must follow the rule and reviews check it by hand, but the check itself is still
  being built (see the roadmap).

## How the bar gates releases

1. **Before 0.5.0 ships, every rule is ✅.** Building the checks is part of the 0.5 milestone
   ([roadmap](ROADMAP.md#quality-bar--the-gate-for-every-minor-from-05-to-09)).
2. **From then on, every minor ships only with the whole bar green on `main`**, and its release notes include the
   benchmark results (Q1.4).
3. Thresholds change only in a PR that says why, never to make a red check pass.

## Baseline: where we start

Measured on 2026-10-18 with a scratch harness over the Shop sample (`InMemoryRuntime`, Release, warm, each order is
`PlaceOrder` + `PaymentConfirmed` to completion; Apple M3 Pro, .NET 10.0.201). Task 014 turns the harness into
`benchmarks/`; until then these numbers are the reference.

| Scenario | Result |
|---|---|
| 20,000 orders on **one SKU** | Throughput falls with every order: **1,197 → 193 orders/s**; on average **2,057 KB allocated per order** |
| 20,000 orders, **one SKU per order** | Flat: **~7,950 orders/s** (16 journal entries per order); **57.7 KB allocated per order** |
| Memory retained after 20,000 orders | **18.3 KB per order, never released** (358 MB) |
| `AdvanceAsync(16 minutes)` after 20,000 completed orders | **1,376 ms**: every completed order left its 15-minute timer behind |
| `dotnet test -c Release` (369 tests) | **~9 s** |
| Undocumented public members in `Wyrd` | **78** (CS1591 is suppressed in `src/Directory.Build.props`) |

What the numbers say:

1. **The engine is not the bottleneck; the sample's model is.** `StockItem.Reservations` never forgets an order that
   shipped. State is stored as a JSON snapshot, so one SKU's state grows forever and is read and written in full on
   every message: each order costs more than the one before. The same mistake in a user's aggregate would do the same.
2. **Timers outlive their waits.** A wait that ends early keeps its timer; the in-memory store sorts every pending
   timer for each one that falls due.
3. **The in-memory host keeps everything**: journal, events, replies, handled ids, instances and their history. Fine
   for a test, a leak for `Shop.Api` running for a week.
4. **One gate serializes the host, and reads wait on it synchronously** (`_gate.Wait()` in `View`, `StatusOf`...):
   blocking on async code inside ASP.NET.
5. **The public API is neither documented nor tracked**: nothing tells a reviewer that a PR changed it.

## The rules

### Q1. Performance is measured, not assumed

| Id | Rule | Check | |
|---|---|---|---|
| Q1.1 | `benchmarks/Wyrd.Benchmarks` (BenchmarkDotNet) covers: the Shop happy path on one SKU and on one SKU per order, a compensated order, and a timer-heavy run (waits that end early and waits that time out). | Builds in CI | 🔜 |
| Q1.2 | **Cost doesn't grow with history.** Bytes allocated per order at 10,000 orders are at most **1.10×** those at 1,000 orders, in every scenario of Q1.1. Allocations, not time: deterministic, so the check never flakes. | CI, fast mode | 🔜 |
| Q1.3 | **Allocation budget.** Bytes allocated per message stay within **+10%** of `benchmarks/baseline.json`. The baseline changes only in a PR that explains the new cost. | CI, fast mode | 🔜 |
| Q1.4 | **Throughput is reported, not gated** (CI machines are noisy): every release note includes orders/s and KB/order for the Q1.1 scenarios, and a PR touching `src/Wyrd/Runtime` pastes before/after numbers. | Release checklist, review | 🔜 |

### Q2. Memory is bounded

| Id | Rule | Check | |
|---|---|---|---|
| Q2.1 | **No state grows without bound per key.** A state, an instance or a history whose size grows with the number of messages it has seen is a bug, in samples and in guides. | Q1.2 on the Shop; review | 🔜 |
| Q2.2 | **A wait's timer is cancelled when the wait ends**, by the awaited event, a compensation or an operator. Documented in [semantics.md](design/semantics.md). | Runtime test; Q1.2 timer scenario | 🔜 |
| Q2.3 | **Everything a host keeps has a documented retention**: inbox (how long a duplicate is still recognised), journal, instance histories, completed instances. Documented in semantics.md, implemented in the in-memory host, configurable. | Benchmark: memory retained after N completed orders stays flat with retention on | 🔜 |

### Q3. The public API changes on purpose

| Id | Rule | Check | |
|---|---|---|---|
| Q3.1 | **Every public member is documented**: what it does and when to use it, for a reader who doesn't know Wyrd. CS1591 is not suppressed under `src/`. | Build (warnings are errors) | 🔜 |
| Q3.2 | **The public API is tracked**: `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` per package (Microsoft.CodeAnalysis.PublicApiAnalyzers). A release moves Unshipped to Shipped. | Build | 🔜 |
| Q3.3 | **Package validation against the last release** (`PackageValidationBaselineVersion`). A breaking change is allowed before 1.0 only when it is listed under *Breaking* in the changelog. | `dotnet pack` in CI | 🔜 |

### Q4. Failures and concurrency are explicit

| Id | Rule | Check | |
|---|---|---|---|
| Q4.1 | **Typed exceptions** for what a host or user handles: concurrency conflicts, unknown message types, missing JSON metadata... derive from `WyrdException`. Argument validation keeps the BCL exceptions. | Tests | 🔜 |
| Q4.2 | **No sync-over-async** under `src/`: no `.Wait()`, `.Result` or `GetAwaiter().GetResult()` on tasks, no blocking waits on a `SemaphoreSlim` in code reachable from async callers. | Analyzer (VSTHRD002 or equivalent) | 🔜 |
| Q4.3 | Every public async method takes a `CancellationToken`. | Review | ✅ (holds today) |

### Q5. Tests stay fast, honest and measured

| Id | Rule | Check | |
|---|---|---|---|
| Q5.1 | `dotnet test -c Release` stays under **30 s** on the reference machine (today ~9 s). Expensive explorations take a budget. | Review | ✅ |
| Q5.2 | **Coverage is reported** for `src/Wyrd` and `src/Wyrd.Testing` in every CI run, and **never decreases from one release to the next** (the floor is the previous release's number). | CI summary; release checklist | 🔜 |
| Q5.3 | `samples/Shop.Tests/SkuldSpecs` green; `samples/Shop.Console` publishes and runs with Native AOT. | CI | ✅ |

### Q6. The samples are the reference

| Id | Rule | Check | |
|---|---|---|---|
| Q6.1 | Samples follow every rule above; the Shop is what the benchmarks run, so a modelling mistake in the sample fails Q1.2. | Q1.2 | 🔜 |
| Q6.2 | Every step declares `.Retry(...)` / `.NoRetry("why")`, every wait `.Within(...)`, every step followed by others `.Undo(...)` / `.NoCompensation("why")`. | Analyzers WYRD0011–0014 | ✅ |

## Who builds what

| Rules | Task | Owner |
|---|---|---|
| Q1.1–Q1.4 | [014 — benchmarks and gates](tasks/014-benchmarks.md) | Codex |
| Q3.1–Q3.3 | [015 — public API tracked and documented](tasks/015-public-api.md) | Codex |
| Q2.1, Q6.1 | [016 — bounded state in the Shop](tasks/016-bounded-shop-state.md) | Codex |
| Q5.2 | [017 — coverage report and ratchet](tasks/017-coverage.md) | Codex |
| Q2.2, Q2.3, Q4.1, Q4.2 | Engine and hosts, with Verdandi | Claude |
