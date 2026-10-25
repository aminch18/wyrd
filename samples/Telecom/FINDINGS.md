# Architectural findings

Evidence is in `samples/Telecom.Tests`, the console recording, and the runnable Compose hosts.
No Wyrd core APIs were changed. These findings distinguish domain policy, sample scope and framework gaps.

1. **Does Wyrd make the domain easier to understand?** Yes at a decision boundary: `ClaimNumberSlice`
   names the serialized MSISDN owner, `Payment.Capture` binds the amount/receipt to the obligation,
   and `Activation.Ready` names the conjunction required for operational service. The self-contained feature folders and named validation guards make these decisions readable without
   reconstructing nested expressions. The sample makes
   confirmation/completion and provisioning/connectivity separate facts. Wyrd does not choose these
   boundaries for us, nor make a correlated process enforce uniqueness across two aggregate keys.

2. **Are processes readable with complications?** The provider call/reply/retry/wait sequences remain
   readable in `Flows.PaymentCollection` and `Flows.ProfileProvisioning`. The cost appears between
   them: evidence forwarding and reassessment require several small processes. `PortEvidence` starts
   for every `NumberChanged`, then its guarded command rejects non-completions. An activation assessment
   that lacks facts similarly compensates an empty one-step process. These are correct executions, but
   `Compensated` in a journal often means “not ready yet”, which is operationally misleading. The
   recorded incident shows that noise. Splitting alternatives keeps a saga linear but does not make
   the resulting graph simple. A union of concrete completion events could reduce some filtering
   plumbing; readiness is still an explicit multi-fact join.

3. **Are invariants at the correct boundaries?** Number ownership is a snapshot keyed by canonical
   MSISDN, profile ownership by profile ID, the obligation by subscription ID, activation by subscription
   ID. PostgreSQL tests put two runtime instances against the same number. Admission seals the reservation in that same number snapshot before downstream work is queued,
   so an uncertain admission cannot race a local release against a future external submission. Epochs retain a high-water
   mark after cancellation and block delayed original commands. Profile generations reject stale probes.
   Billing trusts only the application-owned `ServiceOperationalV1` contract; it does not query an
   eventually consistent screen or independently reconstruct activation policy. This depends on trusted
   command ownership and authenticated event producers in a real deployment. The demo endpoints are local.

4. **Does the current DSL force unnatural modeling?** Partly. Linear orchestration fits fixed steps and
   retry policies. There is no trigger filter, multi-event join or data-dependent schedule in the DSL.
   Readiness lives in a pure aggregate; helper processes reevaluate it. A confirmed porting window is
   stored/displayed, but `Within(2.Days)` is a relative completion deadline, not “execute at the provider's
   window start”. That requirement is deliberately unimplemented rather than encoded as a fake API or
   hidden timer service. A connectivity deadline begins only once the process reaches its wait; a stuck
   provider call can therefore outlive the customer's desired end-to-end deadline. This is a real
   expressiveness gap for business deadlines spanning an in-flight integration step. Fixed technical
   retries are adequate here; no dynamic retry policy or branching was added to the framework.

5. **Can the durable runtime recover from the tested failures?** The PostgreSQL tests verify a committed
   external capture whose worker stops before recording the reply: after lease expiry another runtime
   retrieves the same receipt and stores it once. They also verify a connectivity timer after restart.
   Skuld verifies safety under message perturbations, not crash atomicity or broker behavior. The real
   Compose smoke adds the HTTP and persistent-provider boundary, and found a receipt casing bug missed
   by the direct fake tests. Neither controlled cancellation nor response loss simulates every possible
   process kill, network partition, storage fault, lease overlap or database failover.

6. **Are read models correct under cross-stream reordering?** Independent facts accumulate without
   reset: `PaymentOpened` after `BillingBegan` preserves billing; `SubscriptionOpened` after port/payment
   evidence preserves it. Permutation tests cover those cases with duplicates. Activation events share
   one state stream and therefore rely on Wyrd's ordered projection guarantee; they are not commutative
   arbitrary event bags. The domain rejects superseded profile proofs before they become accepted facts.
   Projection catch-up on an existing deployment and a paged incident index are outside sample scope.

7. **What gets difficult across independently owned microservices?** This implementation is a modular
   application with separate API/worker roles. Its only cross-context public contract is operational
   service evidence for billing; other context handoffs are private domain events. Splitting it requires
   versioned public facts/consumers for paid, completed port and current provisioning attempts, separate
   databases and RabbitMQ subscriptions/outbox relays. Commands do not cross Wyrd's transport: only
   integration events do. An undo and its step must share an inbox owner for fencing, so moving either
   independently changes that guarantee. Subscription identity, authenticated evidence and resource
   ownership still need one authoritative service each. Cross-service joins remain eventually consistent;
   no distributed transaction can make all these state transitions atomic. RabbitMQ already exists in
   Wyrd; no RabbitMQ correctness claim is made by a sample with no broker configured.

8. **Which limitations are domain choices and which are framework gaps?** Irreversible number transfer,
   uncertain provider outcomes and the need to verify connectivity are domain realities. Quarantining
   superseded profiles, refusing post-activation replacement, retaining captures for cancellation review,
   requiring reconciliation before cancellation of an uncertain submission (that inquiry is not implemented),
   and omitting refunds/recurrent billing/deactivation are explicit sample scope choices. Provider-side
   idempotency is a modeled external capability, not Wyrd magic. Data-dependent scheduling, a deadline that
   spans an executing integration, noisy guarded helper processes, no public per-message Skuld invariant
   hook, and no public paged suspended-instance query are framework limitations exposed by this sample.
   The static DSL does not make those business requirements disappear.

## Operational costs visible in current Verdandi

- Worker integration calls use a short database lease transaction, then I/O outside the transaction,
  then a transaction to record the outcome. See `EfCoreRuntime.ProcessOneAsync`/integration handling and
  `docs/design/integrations.md`. A call longer than `IntegrationLease` can overlap another call with the
  same key; provider-side idempotency remains necessary. This sample uses the default five-minute lease.
- `SendAsync` from the API is not a queue-only submit API: it executes the initial decision inline.
  Sending an integration command there, especially with the application's `DbContext` overload, performs
  external I/O inline/in the transaction. The API only sends pure commands; process-generated provider
  commands are left to workers. Two competing API decisions can still retry optimistic conflicts.
- Workers in different processes discover new work by polling, up to `MaxPollInterval` (two seconds by
  default); the wake-up signal is local. Scaling workers does not remove a hot resource's serialization.
- The configured transport relay holds its batch's database transaction while publishing to the broker.
  Slow broker I/O would therefore differ from leased provider I/O. The sample has no broker relay configured;
  benchmark that separately before a service split. Local public events still enter the durable published
  store and expire by the explicit retention policy.
- Queue health counts and `ViewsAsync` scans have a cost. The sample's incident endpoint is not paginated,
  and domain incident projections do not receive automatic process suspension facts. Operators need health,
  individual status/history lookups and a deliberate query layer for a large fleet of suspended instances.
- The sample keeps deduplication 30 days, finished histories/events/dead letters 90 days, relayed messages
  seven days; states and views have business lifetimes. Suspended instances do not expire. Provider logical
  operation receipts and resource leases are business records too. Inbox retention alone cannot enforce
  one logical charge forever; the obligation and provider keys do. Number epochs and profile attempts
  protect late messages after delivery metadata expires. No archive is configured: finished histories
  disappear after retention, so export an incident before then.
- One snapshot per real resource stays bounded per key; lifecycle and capacity of quarantined profiles,
  incident rows and retained customer records still matter. No load benchmark was run to claim a throughput
  envelope. Benchmark contention, queue/backlog age and cleanup/archive cost with production-like latency.

## Verification ledger

The final checked commands and actual outcomes are recorded in the pull request. Reproduce locally with
`dotnet build`, `dotnet test samples/Telecom.Tests` (Docker), the release non-container suite, and the
Compose HTTP smoke. A checked-in history is generated by the console; no journal lines were invented.
