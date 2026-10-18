import type { ReactNode } from 'react'
import styles from '../../styles/site.module.css'
import { Callout } from '../../components/common/Callout'
import { CodeBlock } from '../../components/common/CodeBlock'
import { SNIPPETS } from '../../data/docs'
import { DocArticle } from './DocArticle'
import { Status, type StatusKind } from './Status'

interface DecisionProps {
    title: string
    status: StatusKind
    version?: string
    note?: ReactNode
    problem: ReactNode
    decision: ReactNode
    gives: ReactNode
    costs: ReactNode
    children?: ReactNode
}

/** Renders as direct children of the article so the article styles apply. */
function Decision({
    title,
    status,
    version,
    note,
    problem,
    decision,
    gives,
    costs,
    children,
}: DecisionProps) {
    return (
        <>
            <h2>{title}</h2>
            <p className={styles['decision-status']}>
                <Status kind={status} version={version} />
                {note && <span>{note}</span>}
            </p>
            <p>
                <strong>Problem.</strong> {problem}
            </p>
            <p>
                <strong>Decision.</strong> {decision}
            </p>
            {children}
            <p>
                <strong>What it gives you.</strong> {gives}
            </p>
            <p>
                <strong>What it costs.</strong> {costs}
            </p>
        </>
    )
}

const RETRY_POLICY = `.Then(Stock.Reserve).Retry(3.Times).Undo(Stock.Release)
.Then(Payments.Charge).Retry(3.Times, Backoff.Exponential).Undo(Payments.Refund, 10.Times)
.Then(Shipping.Ship).NoRetry("a human books the carrier; a second attempt would double-book")`

export function DecisionsDoc() {
    return (
        <DocArticle
            eyebrow="Design"
            title="Design decisions"
            lead="The main choices behind Wyrd, each as a problem, a decision, what it gives you and what it costs, so you can judge whether they fit your system."
        >
            <Callout title="Implemented or planned">
                Each decision is marked <Status kind="implemented" />,{' '}
                <Status kind="planned" version="v0.3" /> or{' '}
                <Status kind="limit" />. The reference for the exact rules is{' '}
                <a href="#/docs/semantics">How Wyrd behaves</a>. Wyrd is early
                alpha and the current host is in memory, so read the costs as
                carefully as the benefits.
            </Callout>

            <Decision
                title="Commands, domain events and integration events are different things"
                status="implemented"
                problem="Teams subscribe to another service's internal events. The owner renames a field and production breaks somewhere else. Intentions, internal facts and public promises get the same type, so nothing tells you which can change."
                decision="Three kinds of message, enforced by types and analyzers. A command is an intention with exactly one owner; it is sent and can be rejected. A domain event is a private fact inside one context. An integration event is a public, versioned contract, produced only through an explicit publication mapping."
                gives="You can refactor domain events freely, because only the publication mapping has to keep producing a valid contract. Nobody couples to another context's internals, and the contract catalog can be generated as AsyncAPI."
                costs="More types and a mapping to write for each public event. For a small application with one deployable, that ceremony may add more than it returns."
            />

            <Decision
                title="One owner per command"
                status="implemented"
                problem="If two handlers can answer one intention, nobody owns the rule, and the result depends on which handler happens to run."
                decision="A command has exactly one owning slice. The runtime rejects a second owner, and analyzer WYRD0002 reports it at compile time."
                gives="A single place to read, test and change each decision, and a precise target for retries and the inbox."
                costs="Fan-out is not done with commands. If several parties must react to a fact, that is an event, and each reaction is its own command."
            />

            <Decision
                title="Pure decisions, framework-owned effects"
                status="implemented"
                note="in the in-memory host; real I/O integrations are planned for v0.5"
                problem="A decision that also calls a payment provider cannot know whether a timeout happened before or after the provider accepted the request. Its result then depends on more than its inputs, so it cannot be reproduced, simulated or replayed."
                decision="Deciders and process steps are pure functions. They return facts or intentions. Wyrd owns delivery, persistence, deduplication, retries, timers and compensation, and there is no API that lets user code publish, persist or schedule directly."
                gives="Decisions can be tested without a database or broker, the same code runs in tests and in simulation, and a failing run can be replayed exactly."
                costs="You cannot call an external API from a decision. External calls need their own narrow shape (integrations, planned for v0.5, with idempotency keys and recorded outcomes), so today the framework does not talk to real systems."
            />

            <Decision
                title="Retries are explicit, per step, with no default"
                status="planned"
                version="v0.3"
                note="today, a step without .Retry(...) has no retries"
                problem="A default retry count is a hidden business decision. Retrying a payment is safe only if the owner deduplicates; retrying a booking may not be. Skuld also showed that a step that can be undone but cannot retry leaves stock reserved after a single transient failure."
                decision='Every step declares its policy: .Retry(attempts, backoff) or .NoRetry("why"). There is no default, and an analyzer (WYRD0014) reports a step that declares neither. Retries re-send the same message id, so the owner&apos;s inbox makes them safe.'
                gives="Reviewers see the retry decision for every step, and a missing one is a build warning instead of a surprise in production."
                costs="One more line per step, and you have to be able to say why a step is safe to retry (or not)."
            >
                <CodeBlock
                    code={RETRY_POLICY}
                    title="A policy on every step (planned syntax)"
                />
                <p>
                    <strong>Why undos always insist.</strong> An undo is the
                    last thing between a half-done process and an inconsistent
                    world, so its retries are a guarantee, not an option: a
                    bounded, generous default with backoff, adjustable with{' '}
                    <code>.Undo(command, 10.Times)</code> and impossible to
                    disable. It does not inherit the step&apos;s policy; a step
                    that must not be retried can still have an undo that
                    persists. Today an undo uses its step&apos;s policy.
                </p>
            </Decision>

            <Decision
                title="One retry layer"
                status="implemented"
                note="in the in-memory host; durable hosts are planned for v0.5"
                problem="When a process retries three times and the transport also retries three times, one failure becomes nine attempts that nobody configured and nobody can explain."
                decision="A failure of a process command reaches the process as a failure, and the step's policy is the only decision. Hosts do not multiply attempts behind the process's back; a command that cannot be delivered or handled ends as a failure reply. A business rejection is never retried, because a pure decision would answer the same."
                gives="The number of attempts you read in the process is the number that happens, and simulation and production agree."
                costs="Durable hosts have to be built to this rule (planned for v0.5), and you cannot lean on broker-level retries to hide a missing step policy."
            />

            <Decision
                title="Suspend instead of failing silently"
                status="planned"
                version="v0.3"
                note="today an exhausted undo ends as Failed, and exhausted forward retries compensate"
                problem="When retries run out, there are two bad options: cancel the order (even though the cause may be an outage or a bug in your code) or leave the instance stuck where nobody looks."
                decision="An instance whose step retries are exhausted, whose undo retries are exhausted, whose outcome command fails, or whose definition changed becomes Suspended with a reason. An operator resumes it after a fix or compensates it. A process may opt into compensating automatically instead."
                gives="Nothing is lost and nothing stays stuck in silence: the situation is visible (status, reason, alert) and recoverable."
                costs="Suspending does not make the world consistent; the instance stays half-way until someone acts. Someone has to watch for suspended instances."
            />

            <Decision
                title="Fencing for unknown outcomes"
                status="implemented"
                problem='When a process gives up on a step it cannot know whether the step ran. A delayed copy of the command may still execute later and leave, for example, stock reserved forever. Guessing either way is wrong, and a domain rejection of the undo ("not reserved") only describes the current state.'
                decision="The undo carries the id of the original command. The owner decides in the same transaction as its inbox: if the original ran, the undo runs; if it never ran, the original is fenced so any late copy finds the fence and never executes."
                gives="The process can give up and compensate without knowing, and the world still ends consistent. Skuld found this in Wyrd's own sample shop."
                costs="An undo must be handled by the same service (the same inbox) as its step."
            />

            <Decision
                title="Time limits are business decisions only"
                status="implemented"
                note="the observability for slow steps is planned for v0.5"
                problem="Technical timeouts turn a slow answer into a failure. A payment provider that takes 40 seconds then triggers compensation of an order that would have succeeded."
                decision="Steps have no technical timeouts. Durable messaging waits, and a slow third party only makes a process wait. A deadline is a business choice, modelled as an explicit wait with .Within(duration), and when it expires the process compensates on purpose. A wait without .Within is reported by WYRD0012. Latency is observed, never turned into a failure."
                gives="Every compensation has a business reason you can read in the process."
                costs="A step that never answers waits forever unless you model a confirmation with .Within. Until the observability arrives, slow steps are not surfaced for you."
            />

            <Decision
                title="A structure fingerprint per instance"
                status="planned"
                version="v0.3"
                note="migrating in-flight instances is planned for v1.0"
                problem="Long-running instances outlive deployments. If someone inserts a step, an instance stored at step 3 would continue at what is now a different step 3."
                decision='Each instance records a fingerprint of the structure it started with: the steps, their order and the waits. Configuration such as retries is not part of it. If the deployed structure differs, the instance is suspended ("definition changed").'
                gives="You can change retry counts and backoff freely, and a structural change cannot silently corrupt running instances."
                costs="Until process versioning exists, a structural change suspends in-flight instances and someone has to deal with them."
            />

            <Decision
                title="Processes are linear"
                status="limit"
                problem="Branching makes processes hard to read, simulate and version."
                decision="A process is a trigger, steps and waits in order, with an optional outcome when compensated. An alternative path is another process."
                gives="A process reads top to bottom, draws as one diagram and can be simulated and versioned."
                costs="Some workflows need more than one process, which you then connect through events."
            />

            <Decision
                title="Snapshot state, not event sourcing"
                status="limit"
                problem="Rebuilding state from the full history on every command is a bigger commitment (storage, versioning of old events, replay cost) than many teams need."
                decision="State is stored as a snapshot and events are kept as history. State is not rebuilt from them, so changing Evolve does not change states already stored."
                gives="Simple storage and fast reads. Using events does not require event sourcing."
                costs="Fixing a bug in Evolve does not repair existing states. Event sourcing is a possible future option and is listed as an open decision."
            />

            <Decision
                title="One event per decision"
                status="limit"
                problem="A decision that returns several events makes it unclear which fact each part of the system reacts to."
                decision="A decision produces at most one event. Two facts are modelled as one event, or a union of events."
                gives="Simple, explicit decisions, and a clear mapping from a command to the single fact it caused."
                costs="Some decisions need a union type to express more than one outcome."
            />

            <Decision
                title="At-least-once delivery, effectively-once decisions, one transaction per message"
                status="implemented"
                problem="Networks deliver messages twice. If state and outgoing messages are saved separately, a crash loses one of them."
                decision="Every message is handled in one transaction: state, events, instances, inbox, outbox, timers and read models commit together. Delivery is at least once, and the owner's inbox remembers each command it decided and replays the outcome to duplicates."
                gives="A duplicate never decides twice, and a crash never leaves a state change without its messages."
                costs="Isolation between processes is not guaranteed: others may see intermediate states, such as reserved stock. That is the usual saga trade-off."
            />

            <Decision
                title="Source-generated JSON for Native AOT"
                status="implemented"
                problem="Messages and states cross the queue and the store as JSON. Resolving types by name at runtime with reflection breaks trimming and Native AOT."
                decision="No runtime code generation and no Type.GetType. Types are resolved by registered name and serialized with the app's [WyrdJsonContext]. Reflection lives only behind explicit entry points for development."
                gives="The sample shop publishes and runs as a native executable, checked in CI, and analyzers (WYRD0300 to WYRD0302) keep the context complete."
                costs="You register your message and state types in a JsonSerializerContext."
            >
                <CodeBlock code={SNIPPETS.aotJson} title="ShopJson.cs" />
            </Decision>

            <Decision
                title="Versioned contracts with snapshots"
                status="implemented"
                problem="A public event changes shape and subscribers fail to read it, usually discovered in production."
                decision="Every integration event schema is snapshotted in contracts.wyrd.txt and reviewed in pull requests. A breaking change (a removed or renamed field, a changed type) fails the build with WYRD0201. Evolution is a new version plus an upcaster, so each subscriber receives a fact once, in the version it understands."
                gives="Breaking a public contract by accident is not possible, and the contract catalog is documented for free."
                costs="The check is structural. It cannot see a change in what a field means to the business; that stays in the conversation and the review."
            />

            <Callout title="Not decided here">
                State persistence (state-stored, event-sourced or both) and the
                full rules catalog are still open decisions on the{' '}
                <a href="#/docs/roadmap">roadmap</a>.
            </Callout>
        </DocArticle>
    )
}
