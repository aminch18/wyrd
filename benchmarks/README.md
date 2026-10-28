# Shop benchmarks and allocation gates

These run the real Shop module with its generated JSON context, slices, publications, projections and processes.
Each workload owns a fresh host, uses distinct order IDs, and verifies every fulfillment outcome. Runtime creation,
restocking, command/event processing, outcome checks and disposal are included in the cost.

## Scenarios

- `SingleSku`: all orders reserve one SKU, then confirm payment and ship. This catches state that grows per key.
- `SkuPerOrder`: the same happy path with one SKU per order, separating per-key growth from engine cost.
- `Compensated`: each order's card is declined; its reservation is released and the order is cancelled.
- `Timers`: one SKU per order; half the waits end on confirmation, half time out. All orders are started before
  advancing virtual time by 16 minutes, past both live and obsolete deadlines. Timer draining is measured.

The first three scenarios do not advance time. Their outstanding deadlines are deliberately retained until host
disposal; `Timers` measures draining them. Host retention and timer cancellation belong to the runtime's Q2 work.

## BenchmarkDotNet

```bash
dotnet run -c Release --project benchmarks/Wyrd.Benchmarks -- --filter '*'
# ShortRun: all four cases, 3 warmups and 3 measured iterations.
dotnet run -c Release --project benchmarks/Wyrd.Benchmarks -- --filter '*' --job short
```

`MemoryDiagnoser` reports allocations. Each invocation handles 1,000 orders; `OperationsPerInvoke` normalizes Mean
and Allocated to **one order**, not one batch. `Orders/s`, `Messages/s` and `B/message` supplement that table.
Throughput is information for reviews and release notes, never a CI pass/fail threshold. Run reference measurements
without other CPU-intensive jobs. ShortRun has wide timing confidence intervals; use the default job for comparisons.

A **message** is one envelope handled by the engine: commands, events, replies and timeouts, including setup stock
messages. A separate tracing probe at two and four orders determines fixed setup and per-order message counts for
these deterministic workloads; tracing is **disabled** in every timing/allocation measurement. Nothing is inferred
from journal line counts, and no production internals or process-specific message counts are hard-coded.

## Fast allocation gate

```bash
dotnet run -c Release --project benchmarks/Wyrd.Benchmarks -- --gate
```

Each scenario warms up, then runs at 1,000 and 10,000 orders on separate fresh hosts. The gate uses
`GC.GetTotalAllocatedBytes(precise: true)` across all threads, not BenchmarkDotNet timing or allocations of just the
calling thread. Previous workloads are collected before each measurement. The table reports bytes/order and
bytes/handled envelope; all measured setup costs remain included.

- **Q1.2**: bytes/order at 10,000 must be at most 1.10 times bytes/order at 1,000.
- **Q1.3**: bytes/message at 10,000 must be within +10% of the scenario in `baseline.json`.
- Failures print the rule, scenario, observed values and limit. Exit 1 means a failed rule; invalid arguments exit 2.

Task 016 ([PR #35](https://github.com/aminch18/wyrd/pull/35)) removed the single-SKU growth: CI runs the strict
`--gate`, and every scenario is enforced. `--known-single-sku-failure` remains only to reproduce runs from before it.

## Baseline updates

```bash
dotnet run -c Release --project benchmarks/Wyrd.Benchmarks -- --write-baseline
```

This runs the same workloads and writes actual 10,000-order bytes/message to `benchmarks/baseline.json`. It still
checks scaling and exits 1 if scaling fails; writing a baseline never turns growing state into a passing check.
Changing the baseline requires a PR explaining the new cost, the full gate table and BenchmarkDotNet results.
Do not raise a baseline just to silence a regression. A baseline generated on a different revision must say so.

Initial baseline: `main` at `b621286`, .NET 10.0.201, Apple M3 Pro, Release, 2026-10-19. Real gate run:

| Scenario | B/order at 1,000 | B/order at 10,000 | B/message at 10,000 | 10k/1k |
| --- | ---: | ---: | ---: | ---: |
| SingleSku | 152,568.66 | 1,030,710.05 | 73,621.09 | 6.756 |
| SkuPerOrder | 60,867.56 | 61,937.43 | 3,871.09 | 1.018 |
| Compensated | 60,729.94 | 60,767.73 | 4,051.18 | 1.001 |
| Timers | 71,464.07 | 71,658.76 | 3,771.51 | 1.003 |

That run took 16.08 seconds and exited 1 for Q1.2 SingleSku. The high single-SKU budget records the defect; it is
not an acceptable release target. Task 016 and a downward baseline update remove it before the quality bar is green.

### 0.5 (Verdandi)

Baseline written from `feature/verdandi` merged with `main` (the state #11 releases), .NET 10.0.201, Apple M3 Pro,
Release, 2026-10-24. It is the downward update task 016 asked for (`SingleSku` from 73,621 to 4,400 B/message) and it
records what Verdandi costs the in-memory host per message, measured against `main` at the same moment with the same
gate:

| Scenario | B/message `main` | B/message 0.5 | Change | Orders/s `main` | Orders/s 0.5 | 10k/1k 0.5 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| SingleSku | 4,176 | 4,400 | +5.4% | 6,615 | 5,932 | 1.004 |
| SkuPerOrder | 3,983 | 4,257 | +6.9% | 9,666 | 9,683 | 0.997 |
| Compensated | 4,060 | 4,377 | +7.8% | 18,421 | 17,781 | 0.993 |
| Timers | 3,728 | 4,149 | +11.3% | 9,830 | 10,104 | 0.983 |

What the bytes pay for: retention (each handled id, reply, event and published message keeps when it was recorded,
so it can expire), the cancelled timer of every wait that ends early (Timers), a version on every read model (so
workers never overwrite each other's update), the projected version kept per stream, and the reply an instance keeps
for its operator. The first Verdandi build allocated about 1,200 B/message more than `main`; collections most
messages never use are now created on first write, views are updated without closures, a reply no longer copies the
instance to clear a kept reply that isn't there, and a wait's timer is cleared in the same copy that resumes it.
Orders/s is one gate run each, for reviews: it is never a threshold.

BenchmarkDotNet, `--job short` (3 warmups, 3 iterations: wide intervals, information only), per order:

| Scenario | Mean `main` | Mean 0.5 | Allocated `main` | Allocated 0.5 |
| --- | ---: | ---: | ---: | ---: |
| SingleSku | 127.36 us | 67.70 us | 68.92 KB | 71.66 KB |
| SkuPerOrder | 79.85 us | 150.18 us | 71.76 KB | 79.23 KB |
| Compensated | 97.14 us | 113.11 us | 60.08 KB | 65.33 KB |
| Timers | 115.09 us | 125.18 us | 75.72 KB | 83.00 KB |

## Concurrency on PostgreSQL

`Wyrd.Benchmarks.Concurrency` runs the Shop on the durable host (`EfCoreRuntime`) against a real PostgreSQL: a
container by default (Docker required), or the database of `--connection`. Each run gets a new database.

```bash
dotnet run -c Release --project benchmarks/Wyrd.Benchmarks.Concurrency
dotnet run -c Release --project benchmarks/Wyrd.Benchmarks.Concurrency -- --connection "Host=...;Database=..." --commands 2000 --orders 1000
```

- **Commands** is what an API sees: N callers at once, each sending the next `SendAsync` as soon as its last one
  returned, on the one runtime of the process. *Spread*: every command is a `PlaceOrder` for a different order.
  *Hot*: every command is a `Restock` of the same SKU, so every transaction writes the same row. Latency is per call,
  conflict retries included. *Failed* is a call that still conflicted after `MaxConflictRetries` (5); *typed* means the
  caller got a `ConcurrencyConflictException`, *untyped* a provider exception (there should be none).
- **Workers** is a backlog: every order is placed and paid first, then N workers, each in a runtime of its own (as N
  instances of a service would), handle everything until every order is shipped, which is checked. *Messages* comes from
  the runtime's own `wyrd.messages.handled` counter; *conflicts retried* from its log.

`--profile` shows where a worker's time goes instead: one worker handles a backlog of `--orders` orders, and every
command, commit and EF Core savepoint it runs is counted and timed on the client (a command's time includes its round
trip), next to the cheapest round trip to the server (`SELECT 1`). `--synchronous-commit off` starts the container
with commits that don't wait for the disk, to see what that wait costs (never in production).

Throughput and latency depend on the machine and the database: they are reported, never gated (Q1.4). Run on a quiet
machine; a container on a laptop is not a production database, so compare runs, don't quote them as capacity.

### 0.5 (Verdandi)

PostgreSQL 17.11 in Docker Desktop (8.7 GB), .NET 10.0.5, Apple M3 Pro (11 cores), Release, 2026-10-27, connection
pool of 100:

#### Commands: 2,000 SendAsync calls per run, from N concurrent callers

| Keys | Callers | Commands/s | p50 ms | p95 ms | p99 ms | Conflicts retried | Failed: typed | Failed: untyped |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| spread | 1 | 495 | 1.9 | 2.7 | 4.4 | 0 | 0 | 0 |
| spread | 8 | 2624 | 2.9 | 4.4 | 5.1 | 0 | 0 | 0 |
| spread | 32 | 3624 | 8.7 | 12.2 | 15.1 | 0 | 0 | 0 |
| spread | 128 | 3262 | 38.1 | 49.0 | 55.7 | 0 | 0 | 0 |
| hot (one SKU) | 1 | 652 | 1.5 | 2.0 | 2.4 | 0 | 0 | 0 |
| hot (one SKU) | 8 | 587 | 17.3 | 24.1 | 36.2 | 4,798 | 1,014 | 0 |
| hot (one SKU) | 32 | 497 | 74.6 | 101.9 | 116.4 | 6,344 | 1,271 | 0 |
| hot (one SKU) | 128 | 397 | 322.1 | 584.6 | 697.7 | 6,541 | 1,335 | 0 |

#### Workers: 1,000 orders placed and paid, then handled to the end by N workers

| Keys | Workers | Orders/s | Messages/s | Messages | Conflicts retried |
| --- | ---: | ---: | ---: | ---: | ---: |
| spread | 1 | 29 | 491 | 17,000 | 0 |
| spread | 2 | 51 | 875 | 17,000 | 0 |
| spread | 4 | 74 | 1252 | 17,000 | 0 |
| spread | 8 | 99 | 1676 | 17,000 | 0 |
| hot (one SKU) | 1 | 29 | 466 | 16,001 | 0 |
| hot (one SKU) | 2 | 50 | 794 | 16,001 | 1,994 |
| hot (one SKU) | 4 | 59 | 951 | 16,001 | 5,983 |
| hot (one SKU) | 8 | 63 | 1001 | 16,001 | 13,151 |

What the numbers say:

- Different keys scale with callers until the connection pool is the limit: from 32 to 128 callers throughput stays
  flat and latency grows, since requests wait for one of the 100 connections.
- One key takes one write at a time. With 8 or more callers on it, half or more of the calls run out of retries:
  `SendAsync` retries a conflict at once, so the callers that lost keep colliding with each other.
- Eight workers drain a spread backlog 3.4 times faster than one. On one hot SKU they plateau: the extra workers
  mostly roll back and take turns. Workers never fail on a conflict, they handle the message again.

#### Profile: where one message's time goes

300 orders, one worker, 2026-10-27. The same profile from the macOS host (Docker Desktop forwards every round trip into
its VM) and from a Linux container next to PostgreSQL on the same Docker network:

| | macOS host | Linux container |
| --- | ---: | ---: |
| Cheapest round trip (`SELECT 1`) | 0.152 ms | 0.062 ms |
| Wall time per message | 2.42 ms | 1.69 ms |
| Commands per message | 6.1 (57% of the time) | 6.1 (39%) |
| Commits per message | 1.0 (7%) | 1.0 (15%) |
| EF Core savepoints per message | 1.8 | 1.8 |
| Everything else per message (engine, EF Core, JSON, savepoints) | 0.87 ms | 0.78 ms |

- The disk is not the cost: with `synchronous_commit=off` the commit share stays at 7% and the wall time doesn't drop.
- Round trips are: about 9 per message (6 commands, the commit, and EF Core's automatic savepoint around each
  `SaveChanges`). Every millisecond of latency to the database costs about 9 ms per message.
- The client's own work is the rest, about 0.8 ms per message, half of it in EF Core (query pipeline, change tracking,
  context creation). The engine itself is not visible: in memory the same Shop handles about 10,000 orders/s.
- An order of the Shop is 17 messages, so 17 times all of the above.

#### Cheaper messages on the EF Core host

Three changes, each measured with `--profile` (300 orders, one worker, macOS host; two or three runs each, which differ by
about ±3%):

| Change | Time per message | Commands per message |
| --- | ---: | ---: |
| Before | 2.37–2.43 ms | 6.1, and 1.8 savepoints |
| No automatic savepoints in Wyrd's own transactions | 2.18–2.23 ms | 6.1 |
| The handled message leaves the queue in the batch of its own writes | 2.05–2.18 ms | 5.2 |
| The benchmark's contexts pooled (`PooledDbContextFactory`, what the guide now recommends) | 1.97–2.02 ms | 5.2 |

Compiled queries (`EF.CompileAsyncQuery`) for the hot reads were tried too and changed nothing measurable, so they
were left out.

The full run after them, same machine and day:

| Workers draining 1,000 orders | Before: messages/s | After: messages/s | Change |
| --- | ---: | ---: | ---: |
| spread, 1 worker | 491 | 591 | +20% |
| spread, 8 workers | 1,676 | 2,340 | +40% |
| hot (one SKU), 8 workers | 1,001 | 1,319 | +32% |

| Commands (`SendAsync`) | Before | After |
| --- | ---: | ---: |
| spread, 1 caller: commands/s, p50 | 495, 1.9 ms | 714, 1.3 ms |
| spread, 32 callers: commands/s | 3,624 | 2,782 and 3,483 (two runs) |
| hot, 8 callers: failed after 5 retries | 1,014 | 994 |

Past the connection pool (128 callers) throughput varies between runs (1,882 to 3,262 before; 2,748 to 2,775 after), so
those rows are not compared. Hot keys are unchanged: a conflict is still retried at once.

