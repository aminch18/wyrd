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
