# Verdandi performance: what we know, what we don't, and the plan

Status: **proposal.** In review: 3.3 backoff (#73, measured) and 3.1 the integration dispatcher (#74); the benchmark of
section 2 is being built. Written 2026-10-28 from the concurrency benchmark (#63), the per-message
profile, the cheaper-messages change (#64), an independent review of the "fused local steps" idea, and a look at the
Telecom sample. Every number below says whether it was **measured** or is an **estimate** (counted round trips, not a
run). Nothing here changes `semantics.md` until its own pull request does, on purpose.

## 1. Where we are

### 1.1 Measured (benchmarks/README.md, macOS host, PostgreSQL 17 in Docker Desktop, one process)

| What | Number |
| --- | --- |
| Time per message, one worker, after #64 | 1.97–2.02 ms |
| Commands (SQL statements) per message, after #64 | 5.2, plus the commit |
| Server-side SQL per message | ~0.1 ms (the rest is round trips and client work) |
| Cheapest round trip (`SELECT 1`) | 0.152 ms macOS host, 0.062 ms Linux container |
| Messages per Shop order | 17 (16,001 per 1,000 orders on one hot SKU) |
| Workers draining 1,000 orders, spread keys | 591 msg/s (1 worker), 2,340 msg/s (8 workers) |
| Workers, one hot SKU, 8 workers | 1,319 msg/s |
| `SendAsync`, one hot SKU, 8 callers | 994 of 2,000 calls failed after 5 attempts |
| `synchronous_commit=off` | no gain: the disk is not the cost |

### 1.2 The hot path today (code as of `main`, 2026-10-28)

`EfCoreRuntime.ProcessOneAsync` (`src/Wyrd.EntityFrameworkCore/EfCoreRuntime.cs`), one message per transaction:

1. `NewContextAsync` (pooled factory recommended; automatic savepoints off since #64), `BeginTransactionAsync`.
2. Claim: `SELECT … FROM wyrd.queue WHERE available_at <= @now ORDER BY available_at, id LIMIT 1 FOR UPDATE SKIP LOCKED`
   (SQL Server: `TOP (1) … WITH (UPDLOCK, READPAST, ROWLOCK)`), index `ix_queue_available (available_at, id)`.
3. Engine `HandleAsync` through `EfCoreStore`: separate EF queries, each a round trip, for whatever the message
   needs: `inbox` or `handled` (dedup), `states`, `instances`, `projected` and `held` (projection bookkeeping),
   `views`, `parked`.
4. One `SaveChangesAsync`: versioned `UPDATE`s (`WHERE version = @read`), inserts (events, queue, inbox/handled,
   published, views), and since #64 the delete of the claimed queue row, batched by EF.
5. `CommitAsync`.
6. Integration commands: the row is **leased** (`available_at = now + IntegrationLease`, default 5 min), the
   transaction commits, then `CallLeasedAsync` makes the outside call **awaited inside the worker loop**, then
   records the outcome in a second transaction.

`RunAsync` loops `ProcessOneAsync`; with nothing due it waits `PollInterval` (250 ms) doubling to `MaxPollInterval`
(2 s). A message queued by its own process wakes it (`Wake()`); one queued by another process is seen on the next poll.

`SendAsync(command)` runs one transaction per attempt, `MaxConflictRetries` (5) attempts **immediately one after the
other**, then `ConcurrencyConflictException` (#66).

Internal messages (process commands, replies, domain events, timers, retries) never touch a broker: they are rows of
`wyrd.queue`. RabbitMQ (`IWyrdTransport.SendAsync`) only carries integration events relayed from `wyrd.published`.

### 1.3 What the current benchmark gets wrong

1. **Closed loop.** Each caller sends the next command when the last returns: when the system slows, load drops, so
   latency is under-reported (coordinated omission). Needs an open-loop generator at a fixed arrival rate.
2. **Client and database on one laptop**, through Docker Desktop's VM: CPU contention and inflated round trips.
   "Throughput stops growing at 32 callers" may be the laptop.
3. **One run per row.** 128 callers measured 1,882 and 3,262 commands/s on two runs. Needs ≥5 runs, median and spread.
4. **Workers as N runtimes in one process**, sharing thread pool and GC: not how it is deployed.
5. **PostgreSQL and the Shop only.** No SQL Server, no Telecom.
6. **Hot-key failure rate is an artefact** of immediate retries (no backoff).

### 1.4 Never measured

- End-to-end latency of an order or a port (first command → process `Completed`). This is what users feel.
- Sustained load over time (soak): throughput stability, table growth, cleanup and autovacuum cost with retention on.
- Outbox relay through RabbitMQ: throughput and delay from `published` to the other service.
- Integrations: the lease path with a slow provider (see 3.1, the real Telecom bottleneck).
- Many timers falling due at once; many long-lived instances waiting (Telecom: `within 2d`).
- Recovery: time for another worker to take over after a crash; retries and dead letters under load.
- Host memory and GC under load.
- Effect of PgBouncer, pool sizes, and PostgreSQL settings/indexes.

## 2. The benchmark to build first

Nothing below is worth doing without it: we could not prove any gain.

### 2.1 Topology (docker compose, Linux; reproducible on a real Linux box)

| Container | Limits | Notes |
| --- | --- | --- |
| `postgres` 17 | 4 CPU, 8 GB, fixed `shared_buffers`, default `synchronous_commit` | `log_lock_waits=on`, `pg_stat_statements` |
| `pgbouncer` ≥ 1.21 | 1 CPU | `pool_mode=transaction`, `max_prepared_statements` set |
| `worker-N` (×1, 2, 4, 8) | 1 CPU each | separate **processes**, one `EfCoreRuntime` each, through PgBouncer |
| `api` | 2 CPU | receives the load, calls `SendAsync` |
| `load` | 2 CPU | open-loop generator, fixed arrival rate, HdrHistogram-style percentiles |
| `providers` (Telecom) | 1 CPU | the sample's deterministic provider simulator, latency configurable |
| `sqlserver` 2022 (second matrix) | 4 CPU, 8 GB | `READ_COMMITTED_SNAPSHOT ON` |

Npgsql through PgBouncer (transaction mode): `No Reset On Close=true`; leave auto-prepare off, or enable it only with
`max_prepared_statements` on PgBouncer ≥ 1.21. Wyrd uses no advisory locks, `LISTEN` or session state today, so it is
compatible; item 3.4 (`LISTEN/NOTIFY`) is not, and needs one direct connection per instance.

### 2.2 Scenarios

| Scenario | Keys | Measures |
| --- | --- | --- |
| Shop orders, spread | one SKU per order | orders/s sustained; order latency p50/p95/p99 (PlaceOrder → both processes completed) |
| Shop orders, hot | one SKU for all | same, plus conflicts and failed calls |
| Telecom ports | one port per customer | port latency end to end; provider calls/s; with provider latency 100 ms, 1 s, 5 s |
| Telecom waiting | 100k instances awaiting webhooks | table sizes, timer firing rate when many fall due, webhook ingestion rate |
| Soak | Shop spread, 30 min, retention on (short periods) | throughput over time, table sizes, cleanup cost, autovacuum |
| Recovery | kill a worker mid-load | time until its leased/claimed work is done by others |

### 2.3 Method

- **A raw-PostgreSQL ceiling first:** plain inserts at the same rates on the same database (as DBOS's
  `postgres_insert.py`, section 5), so every Wyrd number reads as a share of what the database itself does.
- **Producers and workers in separate processes**, scaled independently, so a bottleneck on one side is not hidden by
  the other.
- **Cleanup runs during the load** (a separate cleaner, or workers with cleanup on, and short retention periods), with
  its pass times reported: whether it keeps up is part of the result, not a separate test.
- **Progress counted in process** (shared counters per producer and worker), never by querying `wyrd.queue` or
  `wyrd.instances`: a count over the tables under stress changes what is measured.
- A **new database per run**, `pg_stat_statements` enabled on it.
- Warm-up 60 s, measure **15 minutes** for the published points (2 minutes for quick comparisons), **5 runs** per
  point, report median and min–max; a progress line every few minutes (sent, completed, backlog).
- Arrival rates stepped until p99 order latency passes an SLO (say 1 s): the reported capacity is the highest rate that
  meets it, not a peak.
- Per run: round trips per message (EF interceptor, already in `--profile`), `pg_stat_statements` top statements, lock
  waits, CPU per container, GC counts.
- Results in `benchmarks/README.md` with the exact environment; reported, never gated (Q1.4).

## 3. Improvements, in the order to do them

Each one is measured with section 2 before and after, on Shop and Telecom.

### 3.1 Integration calls must not block a worker (Telecom's real bottleneck)

**Problem (verified in code).** `ProcessOneAsync` awaits `CallLeasedAsync`. With N workers and provider latency L,
the service makes at most N/L calls per second, and every other message waits behind them: 8 workers and a 2 s
provider is ~4 calls/s for the whole service.

**Design.** Keep the lease exactly as today. After the lease commits, hand the call to a bounded in-process
dispatcher (`EfCoreRuntimeOptions.MaxConcurrentCalls`, e.g. 64, per runtime) and return to the loop. The dispatcher
makes the call and records the outcome in its own transaction, as `CallLeasedAsync` does now. Shutdown: stop
leasing, wait for in-flight calls up to a timeout; anything not finished is retaken after its lease by any worker,
with the same idempotency key (today's guarantee).

**Semantics.** Unchanged: the lease and second transaction already make the call independent of the worker.
`semantics.md` gains only the concurrency bound.

**Risks.** Back-pressure: when the dispatcher is full, the worker must not lease more integration commands (skip them
in the claim, or wait). Calls in flight count against `IntegrationLease`.

**Expected.** Estimate: provider-bound throughput goes from N/L to `MaxConcurrentCalls`/L, and non-integration
messages stop queueing behind slow calls.

### 3.2 Fewer round trips per message, same semantics

**Problem.** 5.2 statements plus a commit per message; each is a round trip (0.06–0.15 ms each before any work).

**Design.**
- One batch for the reads a message needs: claim + dedup check + state + instance + projection bookkeeping, using
  `NpgsqlBatch` (PostgreSQL) / a multi-statement command (SQL Server), or one statement with a CTE where it fits.
  This leaves EF for the model and migrations and uses raw SQL only on the hot path (`EfCoreStore`).
- Writes stay one batch (already true since #64).
- Measure first where the ~1.9 ms outside the server goes: round trips vs EF query pipeline vs JSON vs engine.

**Semantics.** None. Skuld untouched.

**Expected.** Estimate: from ~6 round trips to 2–3 per message, roughly 1.5–2× messages/s per worker.

### 3.3 Back off conflict retries in `SendAsync`

**Problem.** Five attempts back to back: losers keep colliding (994–1,335 of 2,000 calls fail on a hot key with
8+ callers).

**Design.** Exponential backoff with jitter between attempts (e.g. base 2 ms, cap 100 ms, full jitter). Deterministic
in tests through `TimeProvider` and a seeded jitter source passed by the host (the engine stays free of randomness).

**Expected.** Fewer failed calls on hot keys; latency up for the losers. Complements, does not replace, 3.5.

### 3.4 Wake workers across processes (`LISTEN/NOTIFY`)

**Problem.** A message queued by another process waits up to `MaxPollInterval` (2 s) when the worker was idle; an
order crosses the queue ~16 times.

**Design.** After a commit that queued rows, `NOTIFY wyrd_queue`; each runtime keeps one dedicated listener
connection (**not through PgBouncer**: transaction mode does not support `LISTEN`) and calls `Wake()`. Polling stays
as the fallback. SQL Server: no equivalent; keep polling (or Service Broker, out of scope).

**Expected.** Order latency under light load: possibly most of what fusion (3.6) would win on latency. Measure before
designing 3.6.

### 3.5 Hot aggregates: commit many commands for one key together

**Problem.** A hot aggregate (the SKU every order reserves) takes one write at a time; versioned writes make every
other writer roll back and retry. Measured: 8 workers on one SKU barely beat 2.

**Design A, recommended: group commit per key.** When several commands for the same key are waiting, one transaction
takes them together, runs the pure deciders **one after another on the in-memory state**, and writes the state once:

- Workers: the claim takes up to K due rows whose envelopes target the same `(stateType, key)` (needs the key on
  `wyrd.queue`, indexed) and handles them in `available_at, id` order.
- `SendAsync` in one process: a per-key mailbox; while a transaction for that key is in flight, later callers queue
  behind it and go together in the next one (group commit). Each caller gets its own decision.
- Each command keeps its message id, inbox row, events and reply: the result is exactly handling them one by one in
  that order. A command whose decider throws is taken out and handled alone.

Semantics: unchanged (a valid sequential schedule). Removes conflicts inside a process; across processes they remain,
far fewer.

**Design B, later: key affinity across instances.** Route each key to one owner instance (hash partitioning with
leases), so cross-process conflicts disappear. Infrastructure-heavy (rebalancing, failover).

**Design D, partition concurrency (DBOS's answer, section 5):** at most one message in flight per key across the
whole cluster, enforced by the claim itself: skip due rows whose key already has a claimed row (needs the key on
`wyrd.queue`, indexed, and a claimed marker visible to other workers, e.g. a lease column rather than only a row
lock). No conflicts and no wasted retries, across instances, without routing. Combines with A: the one worker that
holds a key takes all its due rows together. A candidate to replace B.

**Design C, guidance (no framework change):** split the aggregate (stock in buckets, reservations against quotas) and
document the pattern in the guides. Works well with A.

### 3.6 Fused local steps (biggest gain for order latency, most risk; design doc and owner OK first)

**Idea.** When handling a message produces a message for a slice or process **in the same service**, handle it in the
same transaction instead of queueing it, until a real boundary. Shop happy path: 17 worker transactions → ~2
(`OrderPlaced` … awaiting `PaymentConfirmed`; `PaymentConfirmed` … both processes completed). Estimate: ~100 round
trips per order → ~20. Telecom gains less: every provider call is a boundary; its `Store…` steps after a call and
process starts by event still fuse.

**Why it can be safe.** A fused chain is the messages workers would handle one after another; committing a prefix and
leaving the next message queued is a state today's system can reach. Fusion changes **when** results commit, never
what is decided.

**Conditions (from the review; all required):**

1. **Identity and dedup.** Each fused command writes its `inbox` row under the deterministic message id it would have
   had if queued; each fused event writes its `handled` row / stream version. `ResumeAsync`, retries and fencing
   (semantics 3.4) depend on them. Only the reply rows and queue rows are skipped.
2. **Logical time.** A fused message runs at the trigger's "now"; backoff, `.Within` and `available_at` derive from it.
   Both hosts must agree.
3. **Fan-out is a tree.** An event feeding a process start and projections: fixed order, depth-first in production
   order, then registration order, identical in the in-memory and EF hosts. Unrun branches are queued.
4. **Locks that never wait and never raise.** Use try-locks keyed by `(table, key)`:
   `pg_try_advisory_xact_lock(hash)` (PostgreSQL) and `sp_getapplock … @LockTimeout = 0` (SQL Server). They return
   immediately, raise no error (PostgreSQL aborts the whole transaction on any error), work for keys that don't exist
   yet, and are transaction-scoped, so they work behind PgBouncer in transaction mode. Take one for every row a fused
   step writes: `states`, `instances`, and view keys. Not obtained → cut the chain there, queue that step, commit the
   prefix. `SKIP LOCKED` on the state row alone is **not** enough: instance, view and timer-row writes would still
   wait, and `INSERT … ON CONFLICT DO NOTHING` waits on an uncommitted insert of the same key.
5. **In-transaction cache.** A key read twice in one chain must return what the chain wrote, at the version it wrote;
   every store read checks the cache first.
6. **Failure handling.** User code throws (in memory, before writes) → cut, queue the step as an ordinary message with
   its attempt count. Commit fails (connection, unforeseen deadlock) → retry the triggering message **unfused**.
7. **Bounds.** Max steps per transaction; boundaries at integrations, `Await`/timers (an `Await` whose event is
   already parked may continue: decide explicitly), commands to other services, a lock not obtained. Projections onto
   shared views: never fused (hot rows). Compensation chains: fusable with a smaller bound.
8. **SQL Server:** require `READ_COMMITTED_SNAPSHOT`; applocks as above; mind lock escalation.

**Smallest first slice.** Fuse only "process step command to a local slice → its reply back to the same instance",
depth 1, with try-locks on the slice key and the instance; the slice's events and everything else queued as today.
About a third of the Shop's hops, no projections, parked events or timers involved.

**Tests before engine code.** A Skuld property: any fused run, cut at any point, yields a state and journal that some
unfused schedule yields. Skuld gains a "random cut point / random defer" dimension.

**What `semantics.md` (section 4) would say.** A host may handle, in one transaction, messages where one causes the
next. Each keeps its message id, its inbox or `handled` record, its span and its logical time (the trigger's). Order
within the transaction is depth-first in production order. Boundaries: integrations, waits and timers, other services,
a lock not obtained, the step bound. A fused message whose handling fails is queued as an ordinary message with its
attempt count. The result is always one that handling the messages one by one could produce.

### 3.7 Also on the list (smaller, measure first)

- **Claim N independent rows per transaction** (different keys, try-locks): saves N−1 commits; throughput only, no
  latency gain; simpler than fusion (the messages don't cause one another).
- **Integration events relay**: `RelayBatchSize` (100) tuned under load once RabbitMQ is in the benchmark.
- **Indexes and settings**: confirm with `pg_stat_statements` that the claim uses `ix_queue_available`; check bloat
  of `wyrd.queue` (delete-heavy) under the soak and tune autovacuum for it.
- **Escrow or commutative counters** for hot values: a domain-model change; consider after 3.5.
- **Not a library default:** `synchronous_commit=off` can lose acknowledged commits; a deployment knob only.

## 4. Order of work and dependencies

| # | Item | Changes semantics | Needs | Mainly helps |
| --- | --- | --- | --- | --- |
| 1 | Benchmark (section 2) | no | owner OK (infrastructure) | proving everything else |
| 2 | 3.1 Integration dispatcher | no (adds a bound) | 1 | Telecom throughput |
| 3 | 3.2 Fewer round trips | no | 1 | throughput, both samples |
| 4 | 3.3 Backoff | no | 1 | hot-key failures |
| 5 | 3.4 `LISTEN/NOTIFY` | no | 1 | order latency |
| 6 | 3.5 A Group commit per key | no (valid schedule), design doc | 1, owner OK | hot aggregates |
| 6b | 3.5 D Partition concurrency (one in flight per key) | no, design doc | 1, owner OK | hot aggregates across instances |
| 7 | 3.6 Fused local steps | yes, design doc + Skuld property first | 1, 3, 5, owner OK | order latency, throughput |

## 5. What we learn from DBOS

[DBOS](https://github.com/dbos-inc) builds the same thing as Verdandi: durable workflows on PostgreSQL, no broker
(libraries for Python, TypeScript, Go, Java, Rust; `dbosify-py` is a Postgres-backed Temporal replacement). Read on
2026-10-28: the READMEs of `dbos-postgres-benchmark`, `dbos-workflow-benchmarks`, `durable-execution-benchmark`, the
script `dbos-postgres-benchmark/benchmarks/dbos_queue.py`, its `terraform/main.tf`, and in `dbos-transact-py` the
system schema (`dbos/_schemas/system_database.py`), `dbos/_queue.py` and `dbos/_sys_db_postgres.py`. We did **not**
read their claim SQL or any published numbers: nothing here is a claim about their performance.

### 5.1 How they benchmark (`dbos-postgres-benchmark`)

| What they do | What we take |
| --- | --- |
| Open loop at a target rate for 15 minutes: `--rps 1000 --duration 900` | Our method (2.3): fixed arrival rate, 15-minute published points |
| Real hardware through Terraform: PostgreSQL 18 on RDS `db.m7i.4xlarge`, gp3 400 GB; client on a `c7i.24xlarge` (a commented-out `db.m7i.24xlarge` with io2 at 120,000 IOPS) | The published numbers come from a fixed cloud setup, written as code, not a laptop; local compose stays for quick comparisons |
| A raw PostgreSQL insert benchmark next to the DBOS ones | Our ceiling (2.3): Wyrd as a share of what the database does |
| Enqueuers (a client, no runtime) and workers (runtime, no enqueueing) as separate process pools | Producers and workers apart (2.3) |
| A garbage-collector process sweeping finished workflows during the run, with its round times; interval independent from retention | Cleanup measured under load (2.3), and in particular the separate cleaner of #62 |
| Progress kept in shared memory, not queried: "a filtered count over `workflow_status` would scan the table the benchmark is stressing" | Counters in process (2.3) |
| Database dropped and recreated per run, `pg_stat_statements` re-enabled | Same (2.3) |
| Scenarios: start workflow, queue, partitioned queue (`--partitions 1000`), streams | Add a partitioned-keys scenario once D (3.5) exists |

### 5.2 How they are built (`dbos-transact-py`)

- **Steps don't hop through a queue.** A workflow runs as code in the process; each step records its output in
  `operation_outputs` (one write per step). After a crash, the workflow re-runs and skips the steps already recorded.
  The queue (`workflow_status` with `queue_name`) only starts workflows. Our "fused local steps" (3.6) is their
  default behaviour; the models differ (theirs: deterministic code recovered by re-execution; ours: declarative
  processes whose steps are messages), but it confirms that removing per-step queue hops is where order latency goes.
- **Queues with limits:** concurrency per worker and global, rate limits, and **partitioned queues**
  (`queue_partition_key`, `partition_concurrency`, `partition_worker_concurrency`, a limiter per partition). Partition
  concurrency is Design D (3.5) for hot keys.
- **`LISTEN/NOTIFY` is optional** (`use_listen_notify`), with polling as the fallback; the default queue polling
  interval is 1 s. Same shape as 3.4, and optional for the same reason (poolers in transaction mode).
- **Queue features we don't have:** a deduplication id, priority, `delay_until`, debounce, rate limiting. Not
  performance items; worth a roadmap look.

### 5.3 Changes to this plan

- Section 2: raw-PostgreSQL ceiling, separate producer and worker processes, cleanup under load, in-process counters,
  15-minute points (done above).
- Section 3.5: Design D, partition concurrency, as the cross-instance answer for hot keys (done above).
- Section 3.6: DBOS shows the per-step queue hop is avoidable; it does not remove the conditions listed there, which
  come from our message model (dedup ids, logical time, fan-out order, try-locks).

## 6. Open questions for the owner

Decided on 2026-11-03: the benchmark is a Docker Compose setup, run locally to compare changes (before and after, same
machine); the same files run on a fixed Linux machine for numbers worth publishing. A laptop's numbers are never quoted as
capacity.

- Benchmark host: local compose only, or also a fixed Linux machine for the published numbers?
- SLO for "capacity": p99 order latency of 1 s, or another?
- Design A for hot keys: per-process mailbox for `SendAsync` too, or workers only first?
- Fusion: accept "projections onto shared views are never fused"?
- Telecom provider latencies to publish: 100 ms / 1 s / 5 s?
- Published numbers on a fixed cloud setup written as code (as DBOS: RDS + a client instance), or a fixed Linux box?
- Hot keys across instances: Design D (partition concurrency) instead of B (key affinity)?
- DBOS's queue features (deduplication id, priority, delay, debounce, rate limits): worth a roadmap item?
