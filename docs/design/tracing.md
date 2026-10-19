# Design: process traces

> Status: **implemented in v0.5** (in memory; durable hosts carry the same context in the envelopes they store).
> Follows [semantics](semantics.md); when they disagree, semantics wins.

An operator opening a trace sees one process instance as one story: the trigger, every step command, its reply,
retries, waits, timeouts, compensation and the outcome.

## Decisions

### 1. The BCL `ActivitySource`, no OpenTelemetry package

Wyrd instruments with `System.Diagnostics.ActivitySource` named `"Wyrd"`. Hosts collect with `.AddSource("Wyrd")`.

- **Gives:** `src/Wyrd` stays dependency-free; any OpenTelemetry setup (or none) works.
- **Costs:** exporters and sampling are the host's job. The Shop API shows one, opt-in with `OTEL_EXPORTER=console`.

### 2. One consumer activity per handled envelope

Every envelope the engine handles gets one `Consumer` activity named `wyrd <Kind> <Type>`. Correlation reuses the
engine's existing lookups and pure key functions: tracing adds no store reads and no correlation calls of its own.

### 3. The trace context travels with the work, never as part of it

An optional W3C context (`traceparent`, `tracestate`) travels in each envelope, and an instance keeps the context it
started with, so later webhooks, timeouts and operator actions continue its trace. It is diagnostic metadata only:

- trace ids never feed message ids, the journal, domain state, fingerprints or Urd histories;
- with no listener, nothing is captured and no activity is allocated;
- a test compares the Shop's journal, state, histories and message ids with and without a listener, and Skuld runs
  with tracing enabled.

### 4. Integration calls and other services

- An integration's call to the outside world is a `Client` activity, `wyrd integration <Type>`, under its command's
  activity, tagged with `wyrd.idempotency_key` and `wyrd.attempt`; a call that throws has status `Error`. The other
  system's own instrumentation (HTTP, SDKs) nests under it.
- A published integration event keeps the context it was published in. `TransportMessage.TraceParent` /
  `TraceState` carry it (a broker adapter maps them to the W3C headers), and the receiving service continues the
  publisher's trace, as an event inside one service does. Without a listener they stay empty.

### 5. Other traces are links, not parents

A caller from another trace (an HTTP request, a webhook) becomes an `ActivityLink`. An event that feeds several
instances creates one activity and links the other instances' contexts; each command it sends stays in its own
instance's trace. Operator resume and compensate are `Internal` activities.

## Tags and events

| Tag or event | Meaning |
|---|---|
| `messaging.operation`, `messaging.operation.type` | `process` |
| `messaging.message.id` | the envelope id (deterministic) |
| `wyrd.message.kind`, `wyrd.message.type` | envelope kind and registered message name |
| `wyrd.key` | slice or event-stream key, when there is one |
| `wyrd.process.name`, `wyrd.process.key`, `wyrd.process.status` | the correlated instance and its status |
| `wyrd.step`, `wyrd.attempt`, `wyrd.compensating` | the envelope's process reference |
| `wyrd.idempotency_key` | on an integration call: the command's message id, stable across retries |
| `wyrd.outcome`, `wyrd.reply.outcome` | how the envelope was handled, including duplicate, stale, fenced and timeout |
| `wyrd.started`, `wyrd.waiting`, `wyrd.retry`, `wyrd.compensating`, `wyrd.suspended` | activity events for transitions |
| `wyrd.received`, `wyrd.timed_out`, `wyrd.completed`, `wyrd.compensated`, `wyrd.resumed`, `wyrd.compensation_requested` | activity events for recorded history entries |

## Cost

Measured on the PR that introduced tracing (local Release harness, medians of five rounds of 2,000 orders): with no
listener the extra envelope fields cost about 320 B per order (0.56%), with throughput within noise. A listener that
records every activity costs about 32–33 KB per order and 15–27% of throughput.

## Not yet

- Pending-step age and queue-depth metrics come with the hardened queue ([semantics](semantics.md) 3.5).
- The durable relay carries the context to the broker with the EF Core host (Verdandi); in memory it travels today.
