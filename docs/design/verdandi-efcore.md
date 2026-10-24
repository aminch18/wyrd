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
- **Costs:** polling (until the hardened queue, [semantics](semantics.md) 3.5) and database load proportional to the
  message rate.

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

## Not yet

- The hardened queue: `LISTEN/NOTIFY` or backoff polling, batched cleanup, depth and age metrics (semantics 3.5).
- Retention of histories, published rows and handled ids, and catching up a projection added to an existing store.
- A broker package (`Wyrd.RabbitMq`) implementing `IWyrdTransport`.
