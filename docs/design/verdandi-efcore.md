# Design: Verdandi on EF Core

> Status: **implemented in v0.5** (`Wyrd.EntityFrameworkCore`), on PostgreSQL and SQL Server. Follows
> [semantics](semantics.md); when they disagree, semantics wins. Replaces the hand-written Npgsql prototype.

The durable host runs the same engine as the in-memory one. Everything a message changes commits in one database
transaction, together with taking the message off the queue.

## Decisions

### 1. EF Core, not hand-written SQL

The host reads and writes through an EF Core model of Wyrd's tables (`AddWyrd`). The only raw SQL is the claim of the
next message and of the relay's batch, where each database has its own locking hints.

- **Gives:** one host for PostgreSQL and SQL Server; Wyrd's tables in the application's own `DbContext`, migrations
  and, when it wants, transaction.
- **Costs:** EF Core's overhead per message, and EF's release cadence. No Native AOT (decision 6).

### 2. The database is the queue

Queue rows carry the envelope and an availability time; timers and retries are queue rows that become due later. A
worker claims one due row with `FOR UPDATE SKIP LOCKED` (PostgreSQL) or `UPDLOCK, READPAST, ROWLOCK` (SQL Server),
handles it, and deletes it in the same transaction. An integration command is leased instead: its row becomes
available again only after `IntegrationLease`, and its outcome is recorded in a second transaction, by whichever worker
gets there first
([integrations](integrations.md), decision 5).

- **Gives:** no broker inside a service; any number of workers, in one process or many; nothing lost on a crash.
- **Costs:** polling and database load proportional to the message rate.

An idle worker waits `PollInterval` (250 ms), doubling while the queue stays empty up to `MaxPollInterval` (2 s), and is
back to `PollInterval` as soon as there is work. A message this runtime queues itself (`SendAsync`, `PublishAsync`,
`ReceiveAsync`, an operator) wakes its idle workers at once; one queued by another process is seen within
`MaxPollInterval`. `LISTEN/NOTIFY` would remove that delay on PostgreSQL only, so it waits until someone needs it.

Slowness is observed, not turned into failures ([semantics](semantics.md) 3.5): `HealthAsync` measures the queue's lag
(due messages, the oldest one's age), what waits for its time, dead letters, pending steps (`pending_since` on the
instance: when the step it waits on was first sent, retries included), suspended instances, and finished instances
kept past their retention (`ExpiredKept`: cleanup is behind, or the archive keeps failing). Workers publish it every
`HealthInterval` on the `Wyrd` meter (`System.Diagnostics.Metrics`, no dependency), with counters of handled, failed
and dead-lettered messages: alerts belong on these.

### 3. Optimistic concurrency and unique keys settle races

States, instances and read models are written with their expected version in the `WHERE` clause (a view built from
several streams is written by workers handling different streams at once); inbox, handled, parked and held rows, and
new views, are inserted on `SaveChanges`, where a key taken by another worker fails the transaction. A conflict rolls
everything back and the message is handled again against what the other worker committed; it never counts as a
failed delivery.

### 4. One retry layer

A process command the host can't handle goes back to its process as a `Faulted` reply, and the step's retry policy
decides ([semantics](semantics.md) 3.3). Dead letters are only for messages no process decides on: events, replies
and timers, after `MaxDeliveryAttempts`.

### 5. The outbox to other services

An integration event this service publishes is a `published` row, committed with the decision that caused it, with
the message id it keeps end to end and its trace context. The relay claims a batch and marks it relayed **in one
statement** (`UPDATE … RETURNING` / `UPDATE` over a CTE with `OUTPUT`), sends it, and un-marks what the transport
didn't take. A receiving service queues the message with its id (`ReceiveAsync`), so a redelivery is handled once.

- **Gives:** at least once with no window between commit and send; relays in several workers never send a row twice.
  Marking *after* sending deadlocked concurrent relays on SQL Server, and a rolled-back mark resent what was sent.
- **Costs:** a broker hop is a transaction held open while the batch is sent (`RelayBatchSize`, 100 by default).

### 6. JIT only

EF Core 10's Native AOT support is experimental: publishing the Shop API with this host fails on `DbContext` itself,
on `EnsureCreated` and on warnings from EF's own assemblies. The owner decided not to pursue it. The Wyrd core and the
in-memory host stay AOT-compatible; `samples/Shop.Api` builds the durable host only with `-p:WyrdHost=EfCore`.

### 7. Migrations belong to the application

Wyrd ships no migrations: `modelBuilder.AddWyrd(schema)` adds its tables to the application's model, so
`dotnet ef migrations add` includes them, next to the application's own tables. `WyrdDbContext` is the ready context
for an application without one. The Shop API keeps its migrations in `samples/Shop.Api/Durable/Migrations` and applies
them on start; production applies them as a deployment step.

- **Gives:** one migration history per database, reviewed like any other schema change.
- **Costs:** a Wyrd upgrade that changes its tables means a migration in each application, called out in the changelog.

### 8. Retention is explicit

The constructor takes a `Retention` with every member required ([semantics](semantics.md) 4.1): how long duplicates
are recognised, finished instances and their histories kept, relayed messages, domain events and dead letters. Workers
remove what expired every `CleanupInterval`, in batches (`Take(n)` with `ExecuteDelete`, translated by both
providers), each in its own short transaction, so cleanup never holds many locks or slows a message. Dates are
indexed (`finished_at` only for finished instances, a filtered index). Inbox rows that are fences (`fence`) are never
removed. With a history archive, each batch of expired instances, 100 at most whatever the cleanup's batch size (whole
rows are read for their histories), is handed to it first, with no transaction open, and removed only once it
returned; a worker that is stopping doesn't wait for an archive that ignores its token.

- **Gives:** what can expire is bounded by the periods the application chose (fences are the one permanent exception:
  they grow with failures, not traffic); nobody finds out in production what the default was, because there is none.
- **Costs:** a few lines more to set up the host, and a decision the team has to make before starting.

### 9. A new projection catches up from the states

Wyrd keeps current state, not every past event, so a projection added to a store with data is seeded from the states
it names (`Seed<TState>(...)`, [semantics](semantics.md) 5): workers seed each state in its own transaction while the
projection is catching up (`CatchUpAsync`), and then follow its events. A mark per seeded stream (`seeded`) keeps the
version the seed was built from: an event up to it is in the seed and skipped, a later one is applied. The seed checks
the state's version on commit, so an event that changed the state meanwhile wins and the stream is seeded again on the
next pass. Once every state is seeded and every event a seed holds has arrived (`projected` has reached its version),
the projection is caught up (`caught_up`). The marks stay: their key refuses a second seed of a stream, so a worker
that read its batch before another one completed can't seed a view twice, and every instance of the service asks the
store, not its memory, whether a projection caught up. One loop per runtime catches up, a hundred states per pass,
between messages; a `Seed` that fails for one state is logged with the state, and the rest go on. Projected versions
are kept even without projections, so a projection added later knows which events have arrived.

- **Gives:** a read model can be added to a running system without replaying history it doesn't have, and without a
  moment where it shows an event twice or misses one.
- **Costs:** only what a state says can be rebuilt (a view that counts past events starts from what the states show);
  one extra read per event of a seeded stream while the projection catches up, one mark per seeded state, kept, and
  one projected-version write per domain event in applications with no projection.

## Not yet

- `LISTEN/NOTIFY` on PostgreSQL, for messages queued by another process.
