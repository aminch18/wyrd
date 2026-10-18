# Design: Wyrd's semantics

> The source of truth for how Wyrd behaves. Code, Skuld, analyzers and docs follow this document; when they
> disagree, this document wins until it is changed on purpose. Each rule is marked **implemented** or
> **planned (version)**.

## Principles

1. **Decisions are pure; effects belong to Wyrd.** Business code decides; Wyrd delivers, stores, retries,
   compensates and times out.
2. **Explicit over implicit.** No hidden defaults that change behaviour. When something must be decided, the
   compiler asks for the decision (analyzers) instead of assuming one.
3. **Guarantees are not options.** What keeps the system consistent can't be turned off.
4. **Nothing fails silently.** What Wyrd can't resolve becomes visible and recoverable, never lost or stuck in silence.
5. **Time limits are business decisions; slowness is not failure.**

## 1. Messages

| Kind | Meaning | Rules |
|---|---|---|
| Command | An intention | Exactly one owner (slice); sent, never published; can be rejected |
| Domain event | A fact inside one context | Produced only by slices; private; can change freely |
| Integration event | A fact for other contexts | Public, versioned contract; produced only by a publication; snapshot-checked |

All **implemented**.

## 2. Slices

- A command's owner loads the state for `KeyOf(command)`, calls `Decide(command, state)` and, if accepted,
  stores `Evolve(state, event)` with the event. **Implemented.**
- **One decision produces at most one event.** Two facts are modelled as one event (or a union of events).
  *Conscious limit.*
- **State is stored as a snapshot, not event-sourced.** Events are kept as history, but state is not rebuilt from
  them: changing `Evolve` doesn't change states already stored. *Conscious limit; event sourcing is a possible
  future option.*
- Two commands on the same key: optimistic concurrency; one wins, the other is retried and decides again on the
  new state. **Implemented.**

## 3. Processes

A process is **linear**: a trigger, steps and waits in order, and an optional outcome when compensated.
*Conscious limit:* no branching; an alternative path is another process. Linear processes stay easy to read,
simulate and version.

### 3.1 Instances

- One instance per process and key, for the lifetime of the store: a second trigger with the same key is a
  duplicate. **Implemented.**
- Each instance records a **fingerprint of the process structure** it started with: the steps, their order and the
  waits — not configuration such as retries. If the deployed definition's structure differs, the instance is
  **suspended** ("definition changed") instead of continuing at the wrong step. Migrating in-flight instances comes
  with process versioning. **Planned (v0.3); migration planned (v1.0).**

### 3.2 Steps

- A step sends one command and waits for its reply: **accepted** (next step), **rejected** (compensate) or
  **failed** (retry policy). **Implemented.**
- **A step declares its retry policy**: `.Retry(attempts, backoff)` or `.NoRetry("why")`. There is no default;
  an undeclared policy is reported by an analyzer (WYRD0014); without a declaration the step is not retried.
  Retries re-send the same message id, so the owner's inbox makes them safe. **Implemented.**
- **A step says how it is undone**: `.Undo(command)` or `.NoCompensation("why")`; the last step is exempt
  (WYRD0011). **Implemented.**
- A step may read the events produced by earlier steps by asking for them as extra parameters
  (`Ship(OrderPlaced order, PaymentCharged charged)`); a step that asks for an event no earlier step produces makes
  the process invalid. **Planned (v0.3).**

### 3.3 Retries: one layer

- A failure of a process command — the owner can't handle it — **reaches the process as a failure** and the step's
  policy decides. Hosts don't multiply attempts behind the process's back: a command that can't be delivered or
  handled ends as a failure reply. **In memory: implemented. Durable hosts: planned (v0.5).**
- A business rejection is never retried: a pure decision would answer the same. **Implemented.**

### 3.4 Compensation is a guarantee

- When a step is **rejected**, the completed steps are undone in reverse order, then the outcome command
  (`.OnCompensated(...)`) runs. **Implemented.** (Exhausted retries suspend instead: see 3.6.)
- **Undos always insist.** Their retries are a guarantee, not an option: 10 retries with exponential backoff (waits
  capped at a minute) by default, adjustable with `.Undo(command, 20.Times)`, impossible to disable. The outcome
  command insists the same way. **Implemented.**
- A step given up on without knowing whether it ran is **fenced** by its undo: the owner, atomically with its
  inbox, undoes the original command if it ran, or blocks it forever if it didn't. A domain rejection of an undo is
  never taken as proof that there was nothing to undo. **Implemented.** *Requirement:* an undo is handled by the same
  service (inbox) as its step.

### 3.5 Waits and time

- `Await<TEvent>(key)` waits for a fact correlated by key; an event that arrives early is parked until the
  process gets there. **Implemented.**
- **Time limits are business decisions**: `.Within(duration)` on a wait ("the customer has 15 minutes to confirm");
  when it expires, the process compensates on purpose. A wait without `.Within` is reported (WYRD0012).
  **Implemented.**
- **Slowness is not failure.** There are no technical timeouts on steps: durable messaging waits, a slow third party
  only makes processes wait, and latency is *observed* (age of pending steps, alerts), never turned into failures.
  **Observability planned (v0.5).** A business deadline on a step is modelled as an explicit wait for its
  confirmation with `.Within`.

### 3.6 When Wyrd can't resolve something: suspend

An instance is **suspended — needs attention** when:

- a step's retries are exhausted (a failure Wyrd can't classify: an outage or a bug — suspending never cancels an
  order because of a bug; a process may opt into compensating automatically instead),
- an undo's retries are exhausted,
- the outcome command fails after its retries,
- its definition's structure changed (3.1).

Suspending doesn't make the world consistent: the instance stays half-way. It makes the situation **visible**
(status, reason, alert) and **recoverable**: an operator **resumes** it (after a fix) or **compensates** it.
Nothing is lost and nothing stays stuck in silence. `ResumeAsync` re-sends what the instance was doing with the same
message id (a command that did run is not run twice); `CompensateAsync` undoes a forward-suspended instance and
fences the step it gave up on; a late success of the pending command resumes it on its own. **Implemented**
(in memory; durable hosts in v0.5). Opt-in automatic compensation: `.Retry(3.Times).OrCompensate()`. **Implemented.**

### 3.7 Statuses

| Status | Meaning |
|---|---|
| Running | Sending a step or compensating |
| Awaiting | Waiting for an outside fact |
| Completed | Every step done |
| Compensated | Undone, outcome done |
| Suspended | Needs attention, with a reason; resumable or compensable |

`Suspended` carries its reason (`ReasonOf`). It replaced the earlier `Failed`.

## 4. Delivery

- Every message is handled in **one transaction**: state, events, instances, inbox, outbox, timers and read
  models commit together or not at all. **Implemented** in memory; PostgreSQL is built and parked until v0.5.
- Delivery is **at least once**; decisions are **effectively once**: the owner's inbox remembers every command it
  decided and replays the outcome to duplicates. **Implemented.**
- Integration events reach each subscriber **once, in the contract version it understands** (upcast step by step;
  a newer version is never downcast). **Implemented.**

## 5. Read models

- A projection updates a view from events with pure functions, in the transaction that handles the event; the same
  message is never applied twice. **Implemented.**
- Events of one stream (one state key) reach a projection **in the order they happened**, whatever the delivery
  order, so views don't defend against reordering by hand. **Planned (v0.3).**

## 6. Conscious limits

| Limit | Why | Way out |
|---|---|---|
| One event per decision | Keeps decisions simple and explicit | A union of events |
| Linear processes | Readable, simulable, versionable | Another process for the alternative |
| Snapshot state | Simple storage, fast reads | Event sourcing as a future option |
| No real I/O yet | Integrations are designed, not built | Integrations (v0.5): the only place with I/O, with idempotency keys and recorded outcomes |
