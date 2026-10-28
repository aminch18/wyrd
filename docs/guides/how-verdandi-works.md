# How Verdandi works

Verdandi is Wyrd's durable host: `EfCoreRuntime<TContext>` on PostgreSQL or SQL Server. This page explains what
happens underneath, so you can run it in production without reading its code: where every piece of data lives,
what one message does to the database, how many workers and API instances share the work, what fails and how, and
where the limits are. How to set it up is in [Running durable](running-durable.md); the exact rules are in
[How Wyrd behaves](../design/semantics.md).

## The pieces

```text
            your process (one per instance of your API or worker service)
 ┌──────────────────────────────────────────────────────────────────────────┐
 │  endpoints, jobs ──► IWyrdRuntime (one EfCoreRuntime, a singleton)        │
 │                         │  SendAsync / PublishAsync / ReceiveAsync        │
 │                         ▼                                                 │
 │                      Engine: your pure deciders, processes, projections   │
 │                         ▲                                                 │
 │  workers (RunAsync) ────┘  take queued messages, one transaction each      │
 │  cleaner (optional)        removes what the retention no longer keeps      │
 └───────────────────────────────┬──────────────────────────────────────────┘
                                 │ EF Core, one short transaction per message
 ┌───────────────────────────────▼──────────────────────────────────────────┐
 │  your database                                                            │
 │   business data:   states · views                                         │
 │   delivery:        queue · inbox · handled · instances · parked · events   │
 │   to other services: published (the outbox)    failures: dead_letters      │
 └───────────────────────────────┬──────────────────────────────────────────┘
                                 │ relay (optional): IWyrdTransport, e.g. RabbitMQ
                                 ▼
                          other services
```

- **The engine** decides; it never touches storage or the network. Given one message it calls your pure code and
  returns everything that message changed: new state, events, process steps, timers, replies, view updates.
- **The runtime** is the host around the engine. It is one object per process (a singleton): it holds no state of
  its own between calls, only the registry of your slices and processes. Every call opens its own `DbContext` and its
  own transaction.
- **The database is the queue and the outbox.** There is no broker inside a service. Everything the engine produces
  is written as rows, in the same transaction as the state it changed.
- **Workers** are loops (`RunAsync`) that take the next due message from the `queue` table and handle it. Run as many
  as you like, in one process or in many.
- **The cleaner** removes what expired, in the workers or as a service of its own (see *Retention and cleanup*).

## Where the data lives

Wyrd's tables are part of your own `DbContext` (`modelBuilder.AddWyrd()`), in the `wyrd` schema, so they are in your
migrations and can commit in the same transaction as your own tables.

| Table | What it holds | How long |
| --- | --- | --- |
| `states` | The current state of each slice key (an order, a SKU), as JSON, with a version | Always: it is your data |
| `views` | Read models, kept up to date by projections | Always: it is your data |
| `queue` | Every message waiting to be handled: events, commands of processes, replies, timers, retries | Until handled |
| `inbox` | The outcome of every command, by message id: a duplicate gets the same answer | `Deduplication` |
| `handled` | The id of every event handled: a duplicate is ignored | `Deduplication` |
| `instances` | The running and finished processes, with their history | Finished ones: `FinishedInstances` |
| `parked` | An event that arrived before its process was waiting for it | With its instance |
| `events` | The domain events: the record of what happened | `Events` |
| `published` | Integration events for other services (the outbox) | `RelayedMessages` |
| `dead_letters` | Messages that gave up after their delivery attempts | `DeadLetters` |

Business data (`states`, `views`) grows with your business, like any table of yours. Everything else is delivery
machinery: it is needed for a while, and then the retention you chose removes it.

## One order, end to end

The Shop sample's order, from the HTTP request to the shipment:

```text
POST /orders ──► SendAsync(PlaceOrder)
                  BEGIN
                    inbox: seen this message id?  no
                    states: read the order, decide (your pure code), write it    (version checked)
                    events: OrderPlaced
                    queue:  OrderPlaced, to be handled by workers
                    inbox:  the outcome
                  COMMIT ──► 202 Accepted, with the decision

worker ──► queue: OrderPlaced
            BEGIN  starts the fulfillment process, queues ReserveStock, updates the views  COMMIT
worker ──► queue: ReserveStock
            BEGIN  the SKU's state decides, StockReserved, a reply to the process          COMMIT
worker ──► queue: the reply
            BEGIN  the process moves on: ChargePayment is queued                            COMMIT
worker ──► queue: ChargePayment, then its reply
            BEGIN  the process waits for PaymentConfirmed: a timer row, due in 15 minutes   COMMIT

payment confirmed ──► PublishAsync(PaymentConfirmed) ──► queue
worker ──► BEGIN  the wait ends, its timer row is deleted, ShipOrder is queued              COMMIT
worker ──► ... until the process completes
```

`SendAsync` only handles the command itself and returns its decision: accepted or rejected, with your error. What the
command causes is queued in the same transaction and handled by workers right after, so the request never waits for
the rest of the process.

## One message, one transaction

Every message is handled in one transaction that also takes it off the queue. Integrations are the exception: a queued
call to another system is made with no transaction open ([Duplicates](#duplicates)), and an integration command sent
with `SendAsync(command, context)` calls the other system inside your transaction.

```text
BEGIN
  take the next due row of queue        FOR UPDATE SKIP LOCKED (PostgreSQL) / UPDLOCK, READPAST (SQL Server)
  check inbox / handled                 a duplicate is answered, or ignored
  read the state, decide, write it      UPDATE ... WHERE version = <read>
  write events, queue rows, views, the process instance, the inbox row
  delete the queue row
COMMIT
```

Either all of it happens or none of it does. A crash before `COMMIT` leaves the row in the queue, unlocked, and
another worker handles it. There is no state where the order is saved but its event is lost, or the event is sent
but the order isn't saved.

## Many workers, many instances

`SKIP LOCKED` makes every worker pass over rows another worker is handling, instead of waiting for them. That is how
any number of workers share one queue table without a broker: in one process (call `RunAsync` several times) or in
every instance of your service.

A worker that finds nothing waits 250 ms, doubling up to 2 s while the queue stays empty, and is back to full speed as
soon as there is work. A message queued by its own process wakes it at once; one queued by another process is seen on
its next look.

## Duplicates

Messages are delivered at least once: a worker can crash after doing the work and before committing, a broker can
deliver twice. Effects happen once, for as long as the `Deduplication` retention remembers a message id:

- **Commands**: the `inbox` keeps each command's outcome by message id. A duplicate gets the stored answer and changes
  nothing. Two workers racing on the same command both try to insert the inbox row: one wins, the other rolls back.
- **Events**: the `handled` table remembers each event's id; a duplicate is ignored.
- **Calls to other systems** (integrations): the call is made with no transaction open, under a lease, and its outcome
  recorded in a second transaction. The integration gets an idempotency key, the same on every attempt: pass it to
  the other system, and if that system honours it, a second call never repeats the charge.

A caller retrying is not a duplicate: every `SendAsync` is a new message with a new id. If your client may send the
same command twice (a lost HTTP response, a retry), make the command idempotent in your domain: a `PlaceOrder` for an
order that exists is rejected, not placed again.

## When things fail

- **Two writers on the same row.** States and instances are written with `WHERE version = <read>`. The loser of a race
  rolls back and handles the message again against what the winner wrote. It is not a failure: nothing is counted, and
  nothing is lost.
- **A command sent from your code** (`SendAsync`) that conflicts is retried at once, up to `MaxConflictRetries` (5).
  After that the caller gets a `ConcurrencyConflictException`, and nothing was written.
- **A process step that fails** goes back to its process, which applies the step's own retry policy (`.Retry(...)`).
  When the retries run out the instance is suspended for an operator, who resumes or compensates it; a step your
  code *rejects* (a business decision) is compensated at once. The host never retries a process command behind the
  process's back: there is one retry layer.
- **Anything else that throws** (an event handler, a reply) is tried again later, with a growing delay, and after
  `MaxDeliveryAttempts` (10) it moves to `dead_letters` for someone to look at.
- **The database is down.** Workers log it and try again; queued messages are safe in the table.

## Timers

A wait's timeout is just a row in the `queue` whose availability time is the deadline. When the awaited event arrives,
the same transaction deletes that row. When the deadline comes first, a worker takes the row like any other message
and the process times out. Retries with a backoff are queue rows too. Nothing lives in memory, so a restart loses no
timer.

## Concurrency: what scales and what doesn't

The runtime being a singleton is not a bottleneck: it shares nothing between calls. Two things decide how much load a
service takes:

- **Connections.** Every `SendAsync` and every worker transaction uses a connection from the pool while it runs.
  Npgsql's pool has 100 connections by default; requests beyond that wait for one.
- **Hot keys.** Requests for different keys (different orders) don't touch the same rows, and run in parallel. Requests
  for the *same* key (one SKU everybody buys, one account) are serialized by the database: each transaction reads the
  row, decides, and writes it with a version check, so only one of them wins at a time and the others retry.

Measured with `benchmarks/Wyrd.Benchmarks.Concurrency` (the Shop on PostgreSQL in a container, one process; the full
results, and where one message's time goes, are in `benchmarks/README.md`):

Commands from an API: 2,000 `SendAsync` calls per run, N callers at once (PostgreSQL 17 in Docker, .NET 10, Apple
M3 Pro, 2026-10-27, with a pooled context factory):

| Keys | Callers | Commands/s | p50 | p99 | Failed after 5 retries |
| --- | ---: | ---: | ---: | ---: | ---: |
| a different order each | 1 | 714 | 1.3 ms | 2.7 ms | 0 |
| a different order each | 8 | 2,824 | 2.7 ms | 4.8 ms | 0 |
| the same SKU | 1 | 700 | 1.3 ms | 2.8 ms | 0 |
| the same SKU | 8 | 596 | 15.5 ms | 32.2 ms | 994 (50%) |
| the same SKU | 128 | 498 | 259.9 ms | 573.2 ms | 1,333 (67%) |

With different keys, throughput levels off at about 2,800 to 3,600 commands/s from 32 callers on, and past 100 callers
the requests wait for a connection of the pool; those rows vary too much between runs to quote one number.

Workers draining a backlog of 1,000 orders placed and paid (17 messages per order, each its own transaction):

| Keys | Workers | Orders/s | Messages/s |
| --- | ---: | ---: | ---: |
| one SKU per order | 1 | 35 | 591 |
| one SKU per order | 8 | 138 | 2,340 |
| the same SKU | 8 | 82 | 1,319 |

Workers never fail on a conflict: they roll back and handle the message again, so a hot key only costs them time.

What it means in practice:

- Different keys scale with callers until the pool and the database are saturated.
- One key takes one write at a time, whatever the number of callers. When many callers hit the same key at once, some
  run out of retries and get a `ConcurrencyConflictException`: retry later, or model the hot spot differently (split
  the stock of a SKU into buckets, or let a process serialize the requests).
- More workers drain a backlog faster when its messages are for different keys; on one hot key they mostly take turns.
- **Latency to the database is what a message costs.** PostgreSQL itself spends about 0.1 ms on one; the rest is
  round trips (about 6 per message, the commit included) and the client's own work (about 0.6 ms: the engine, EF Core and JSON).
  Keep the database close to the workers, and register the context factory pooled (`AddPooledDbContextFactory`).

## Retention and cleanup

Delivery machinery is removed when the [retention](running-durable.md) you chose says so: duplicates are recognised for
`Deduplication`, finished instances stay for `FinishedInstances`, and so on. The host doesn't start without a
retention, and some things never expire: a suspended instance waits for an operator, and a fence (a command given up
on without knowing whether it ran) stays blocked forever.

Cleanup deletes in batches of 1,000 rows, each its own short statement, so it never holds many locks. It runs in one
of two places:

- **In the workers** (the default, `CleanupMode.Workers`): every minute, each process's workers remove a bounded
  amount between messages. Simple, and right for one instance or a few.
- **In a service of its own** (`CleanupMode.Separate`): workers only handle messages, and one cleaner
  (`RunCleanupAsync`, or `ForgetExpiredAsync` from a scheduled job) removes what expired. With many instances of an
  API, they don't all clean the same tables at once.

With a history archive (`IHistoryArchive`), a finished instance's history is handed to it before the row is removed,
and the row is removed only once the archive returned: a failed archive keeps it for the next pass. Delivery is at least
once, so an archive must accept the same history twice. It goes on the runtime that cleans: with a separate cleaner,
register it there.

## What to watch

Workers publish the queue's health on the `Wyrd` meter: the queue's lag, steps waiting for a slow third party,
instances that need an operator, dead letters, finished instances kept past their retention, and counters of handled,
failed and dead-lettered messages and of rows removed by cleanup. The instruments and what to alert on are in [Running durable](running-durable.md#health).
