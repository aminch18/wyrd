# Architecture decision: telecom stress sample

The sample uses current Wyrd APIs without core changes.
This is one telecom application, with six logical contexts and one database owner; API and worker
are two deployments of that application, not independent microservices. Three provider simulators
have their own persistence. Splitting telecom into independently owned services is a documented
follow-up, not a distributed guarantee hidden behind a shared database.

| Invariant | Owner / serialized key |
|---|---|
| One live port per number | NumberPort / canonical E.164 MSISDN, with increasing epoch |
| One customer per profile | ProfileLease / profile ID, retained until verified deactivation |
| One capture per obligation | Payment / subscription ID; provider also deduplicates the logical obligation |
| Activation requires paid + completed port + current provisioned profile + matching connectivity | Activation / subscription ID |
| Billing starts only on operational evidence | Billing / subscription ID; only Activation publishes that evidence |
| Confirmed is not completed; cancellation is not rollback | NumberPort transition rules |
| Old profile confirmations cannot activate a replacement | Activation's monotonically increasing attempt + profile identity |

Start on SubscriptionOpened, claim the number and apply the admission policy, then start independent payment orchestration.
An admission rejection compensates only the unsubmitted reservation; no provider has been called.
Capture is irreversible in this sample: uncertain capture suspends for reconciliation, never
pretends to void a possible payment. Paid starts MNP submission and initial profile assignment.
Provider notices are versioned integration events, translated by small processes into pure
commands. Port confirmation carries a window for display and validation; the current DSL cannot
schedule a step at a timestamp read from an event. Completion is a provider fact, not a local timer.

Profile replacement explicitly increments the attempt, claims a different profile and provisions it.
Old profiles stay quarantined: recycling needs provider-confirmed deactivation, deliberately not
implemented as a local release. A connectivity deadline opens an incident, not a fictitious undo.
Activation accumulates independent facts; separate billing consumes the operational contract.
Process correlation is not a uniqueness mechanism. Resource slices and provider constraints are.

Each business feature is physically self-contained under `Telecom/Features`: decisions, processes,
provider integrations, projections, contracts and MinApiLib endpoints sit together. API and worker
projects only bootstrap that module. The shared Wyrd database schema, HTTP provider protocol and
composition root live under `Telecom/Infrastructure`; they do not define separate feature layers.
The module is JIT and references its presentation/persistence frameworks. Pure domain functions do
not call them. No separate domain/API folder trees or source-linking conventions are needed.
