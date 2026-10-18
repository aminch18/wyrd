import styles from '../../styles/site.module.css'
import { Callout } from '../../components/common/Callout'
import { CodeBlock } from '../../components/common/CodeBlock'
import { SNIPPETS } from '../../data/docs'
import { DocArticle } from './DocArticle'
import { RuleTable, Status } from './Status'

const PRINCIPLES = [
    [
        'Decisions are pure; effects belong to Wyrd.',
        'Business code decides. Wyrd delivers, stores, retries, compensates and times out.',
    ],
    [
        'Explicit over implicit.',
        'No hidden defaults that change behaviour. When something must be decided, the compiler (analyzers) asks for the decision instead of assuming one.',
    ],
    [
        'Guarantees are not options.',
        'What keeps the system consistent cannot be turned off.',
    ],
    [
        'Nothing fails silently.',
        'What Wyrd cannot resolve becomes visible and recoverable, never lost or stuck in silence.',
    ],
    [
        'Time limits are business decisions; slowness is not failure.',
        'A deadline is something the business chooses on purpose. A slow answer only makes a process wait.',
    ],
] as const

const STATUSES = [
    ['Running', 'Sending a step or compensating.'],
    ['Awaiting', 'Waiting for an outside fact.'],
    ['Completed', 'Every step done.'],
    ['Compensated', 'Undone, and the outcome command ran.'],
    [
        'Suspended',
        'Needs attention, with a reason; it can be resumed or compensated. Planned in v0.3: until then an exhausted undo ends as Failed.',
    ],
] as const

const LIMITS = [
    [
        'One event per decision',
        'Keeps decisions simple and explicit.',
        'Model it as a union of events.',
    ],
    [
        'Linear processes',
        'Readable, simulable and versionable.',
        'Another process for the alternative path.',
    ],
    [
        'Snapshot state',
        'Simple storage and fast reads.',
        'Event sourcing is a possible future option.',
    ],
    [
        'No real I/O yet',
        'Integrations are designed, not built.',
        'Integrations in v0.5: the only place with I/O, with idempotency keys and recorded outcomes.',
    ],
] as const

const PROCESS_SHAPE = `trigger event (correlated by key)
        │
        ▼
 step 1: send a command ──► Accepted ──► next step
        │                └─► Rejected ──► compensate (never retried)
        │                └─► Failed   ──► the step's retry policy decides
        ▼
 wait:  Await<Event>(key) ──► fact arrives ──► next step
        │                 └─► .Within(...) expires ──► compensate on purpose
        ▼
 step 2 ...
        ▼
 last step ──► Completed`

const COMPENSATION_FLOW = `Rejected at step 3
  undo step 2  (insists until it works, or suspends)
  undo step 1
  run the outcome command  (.OnCompensated(...))
  instance: Compensated`

const FENCE_FLOW = `gave up on ChargePayment (m1), outcome unknown
  send Undo(original = m1) to the same owner
  owner, in one transaction with its inbox:
     m1 ran?       yes ──► undo it
                   no  ──► fence m1: a late copy never executes
  a domain rejection of the undo is NOT proof that nothing ran`

const LIFECYCLE = `            ┌───────────── fact arrives ─────────────┐
            ▼                                        │
 trigger ──► Running ──► Awaiting ───────────────────┘
               │            │
               │            └── .Within expires ──┐
               ▼                                   ▼
          Completed                      compensating (Running)
                                                   │
                                                   ▼
                                             Compensated

 retries exhausted, undo exhausted,
 outcome failed, definition changed ──► Suspended
 (planned in v0.3)                        │ resume or compensate`

export function SemanticsDoc() {
    return (
        <DocArticle
            eyebrow="Design"
            title="How Wyrd behaves"
            lead="The rules behind the API: what a message is, what a process promises, and what happens when something goes wrong."
        >
            <Callout title="How to read the status markers">
                This page follows the repository design document, which marks
                every rule as <Status kind="implemented" />, as{' '}
                <Status kind="planned" version="v0.3" /> (with the version that
                plans it), or as a <Status kind="limit" /> that is a deliberate
                boundary. A planned rule is a design decision, not a claim about
                the current package. Where it matters, the table says what
                happens today.
            </Callout>

            <h2>Five principles</h2>
            <ol>
                {PRINCIPLES.map(([rule, why]) => (
                    <li key={rule}>
                        <strong>{rule}</strong> {why}
                    </li>
                ))}
            </ol>

            <h2>Messages</h2>
            <p>
                Three kinds of message exist because they make different
                promises. Mixing them is the most common source of coupling in
                event-driven systems.
            </p>
            <div className={styles['doc-table']}>
                <table>
                    <thead>
                        <tr>
                            <th>Kind</th>
                            <th>Meaning</th>
                            <th>Rules</th>
                            <th>Status</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr>
                            <td>Command</td>
                            <td>An intention</td>
                            <td>
                                Exactly one owner (a slice); sent, never
                                published; can be rejected.
                            </td>
                            <td className={styles['status-cell']}>
                                <Status kind="implemented" />
                            </td>
                        </tr>
                        <tr>
                            <td>Domain event</td>
                            <td>A fact inside one context</td>
                            <td>
                                Produced only by slices; private; can change
                                freely.
                            </td>
                            <td className={styles['status-cell']}>
                                <Status kind="implemented" />
                            </td>
                        </tr>
                        <tr>
                            <td>Integration event</td>
                            <td>A fact for other contexts</td>
                            <td>
                                Public, versioned contract; produced only by a
                                publication; snapshot-checked.
                            </td>
                            <td className={styles['status-cell']}>
                                <Status kind="implemented" />
                            </td>
                        </tr>
                    </tbody>
                </table>
            </div>
            <CodeBlock code={SNIPPETS.messages} title="Messages.cs" />

            <h2>Slices</h2>
            <p>
                A slice owns one command: it decides from the current state and
                says how the state changes. Nothing else touches that state.
            </p>
            <RuleTable
                rows={[
                    {
                        rule: (
                            <>
                                The owner loads the state for{' '}
                                <code>KeyOf(command)</code>, calls{' '}
                                <code>Decide(command, state)</code> and, if
                                accepted, stores{' '}
                                <code>Evolve(state, event)</code> together with
                                the event.
                            </>
                        ),
                        status: 'implemented',
                    },
                    {
                        rule: (
                            <>
                                Two commands on the same key use optimistic
                                concurrency: one wins, the other is retried and
                                decides again on the new state.
                            </>
                        ),
                        status: 'implemented',
                    },
                    {
                        rule: (
                            <>
                                <strong>
                                    One decision produces at most one event.
                                </strong>{' '}
                                Two facts are modelled as one event, or a union
                                of events.
                            </>
                        ),
                        status: 'limit',
                    },
                    {
                        rule: (
                            <>
                                <strong>State is stored as a snapshot</strong>,
                                not rebuilt from events. Events are kept as
                                history, but changing <code>Evolve</code> does
                                not change states already stored.
                            </>
                        ),
                        status: 'limit',
                    },
                ]}
            />

            <h2>Processes</h2>
            <p>
                A process is <strong>linear</strong>: a trigger, steps and waits
                in order, and an optional outcome when it is compensated. There
                is no branching; an alternative path is another process. That is
                a <Status kind="limit" />: linear processes stay easy to read,
                simulate and version.
            </p>
            <CodeBlock
                code={PROCESS_SHAPE}
                language="text"
                title="Shape of a process"
            />

            <h3>Instances and the structure fingerprint</h3>
            <RuleTable
                rows={[
                    {
                        rule: (
                            <>
                                One instance per process and key, for the
                                lifetime of the store. A second trigger with the
                                same key is a duplicate.
                            </>
                        ),
                        status: 'implemented',
                    },
                    {
                        rule: (
                            <>
                                Each instance records a{' '}
                                <strong>fingerprint of the structure</strong> it
                                started with: the steps, their order and the
                                waits, but not configuration such as retries. If
                                the deployed definition has a different
                                structure, the instance is{' '}
                                <strong>suspended</strong> (&quot;definition
                                changed&quot;) instead of continuing at the
                                wrong step.
                            </>
                        ),
                        status: 'planned',
                        version: 'v0.3',
                    },
                    {
                        rule: (
                            <>
                                Migrating in-flight instances (process
                                versioning).
                            </>
                        ),
                        status: 'planned',
                        version: 'v1.0',
                    },
                ]}
            />

            <h3>Steps</h3>
            <RuleTable
                rows={[
                    {
                        rule: (
                            <>
                                A step sends one command and waits for its
                                reply: <strong>accepted</strong> (next step),{' '}
                                <strong>rejected</strong> (compensate) or{' '}
                                <strong>failed</strong> (the retry policy
                                decides).
                            </>
                        ),
                        status: 'implemented',
                    },
                    {
                        rule: (
                            <>
                                <strong>
                                    A step declares its retry policy
                                </strong>
                                : <code>.Retry(attempts, backoff)</code> or{' '}
                                <code>.NoRetry(&quot;why&quot;)</code>. There is
                                no default, and an undeclared policy is reported
                                by an analyzer (WYRD0014). Retries re-send the
                                same message id, so the owner&apos;s inbox makes
                                them safe.
                            </>
                        ),
                        status: 'planned',
                        version: 'v0.3',
                        today: (
                            <>
                                <code>.Retry(...)</code> works, and a step
                                without it simply has no retries.
                            </>
                        ),
                    },
                    {
                        rule: (
                            <>
                                <strong>A step says how it is undone</strong>:{' '}
                                <code>.Undo(command)</code> or{' '}
                                <code>.NoCompensation(&quot;why&quot;)</code>.
                                The last step is exempt (WYRD0011).
                            </>
                        ),
                        status: 'implemented',
                    },
                    {
                        rule: (
                            <>
                                A step may read the events produced by earlier
                                steps by asking for them as extra parameters,
                                for example{' '}
                                <code>
                                    Ship(OrderPlaced order, PaymentCharged
                                    charged)
                                </code>
                                . A step that asks for an event no earlier step
                                produces makes the process invalid.
                            </>
                        ),
                        status: 'planned',
                        version: 'v0.3',
                    },
                ]}
            />

            <h3>Retries: one layer</h3>
            <RuleTable
                rows={[
                    {
                        rule: (
                            <>
                                A failure of a process command (the owner cannot
                                handle it) reaches the process as a failure, and
                                the step&apos;s policy decides. Hosts do not
                                multiply attempts behind the process&apos;s
                                back: a command that cannot be delivered or
                                handled ends as a failure reply.
                            </>
                        ),
                        status: 'implemented',
                        today: (
                            <>
                                true in the in-memory host. Durable hosts are
                                planned for v0.5.
                            </>
                        ),
                    },
                    {
                        rule: (
                            <>
                                A business rejection is never retried: a pure
                                decision would answer the same.
                            </>
                        ),
                        status: 'implemented',
                    },
                ]}
            />

            <h3>Compensation is a guarantee</h3>
            <CodeBlock
                code={COMPENSATION_FLOW}
                language="text"
                title="Compensation"
            />
            <RuleTable
                rows={[
                    {
                        rule: (
                            <>
                                When a step is <strong>rejected</strong>, the
                                completed steps are undone in reverse order,
                                then the outcome command (
                                <code>.OnCompensated(...)</code>) runs.
                            </>
                        ),
                        status: 'implemented',
                        today: (
                            <>
                                exhausted forward retries also compensate.
                                Version 0.3 changes that: they suspend instead.
                            </>
                        ),
                    },
                    {
                        rule: (
                            <>
                                <strong>Undos always insist.</strong> Their
                                retries are a guarantee, not an option: a
                                bounded, generous default with backoff,
                                adjustable with{' '}
                                <code>.Undo(command, retry: ...)</code> and
                                impossible to disable. The diagram shows it.
                            </>
                        ),
                        status: 'planned',
                        version: 'v0.3',
                        today: <>an undo uses its step&apos;s policy.</>,
                    },
                    {
                        rule: (
                            <>
                                A step given up on without knowing whether it
                                ran is <strong>fenced</strong> by its undo (see
                                below). A domain rejection of an undo is never
                                taken as proof that there was nothing to undo.
                                Requirement: an undo is handled by the same
                                service (inbox) as its step.
                            </>
                        ),
                        status: 'implemented',
                    },
                ]}
            />
            <p>
                Fencing exists because a process that gives up on a step cannot
                know whether the step ran: a delayed copy may still execute
                later. The undo carries the id of the original command, and the
                owner decides atomically with its inbox.
            </p>
            <CodeBlock code={FENCE_FLOW} language="text" title="Fencing" />

            <h3>Waits and time</h3>
            <RuleTable
                rows={[
                    {
                        rule: (
                            <>
                                <code>Await&lt;TEvent&gt;(key)</code> waits for
                                a fact correlated by key. An event that arrives
                                early is parked until the process gets there.
                            </>
                        ),
                        status: 'implemented',
                    },
                    {
                        rule: (
                            <>
                                <strong>
                                    Time limits are business decisions
                                </strong>
                                : <code>.Within(duration)</code> on a wait
                                (&quot;the customer has 15 minutes to
                                confirm&quot;). When it expires, the process
                                compensates on purpose. A wait without{' '}
                                <code>.Within</code> is reported (WYRD0012).
                            </>
                        ),
                        status: 'implemented',
                    },
                    {
                        rule: (
                            <>
                                <strong>Slowness is not failure.</strong> There
                                are no technical timeouts on steps: durable
                                messaging waits, and a slow third party only
                                makes processes wait. Latency is{' '}
                                <em>observed</em> (age of pending steps,
                                alerts), never turned into a failure. A business
                                deadline on a step is modelled as an explicit
                                wait for its confirmation with{' '}
                                <code>.Within</code>.
                            </>
                        ),
                        status: 'implemented',
                        today: (
                            <>
                                steps have no timeouts. The observability that
                                surfaces slow steps is planned for v0.5.
                            </>
                        ),
                    },
                ]}
            />

            <h3>When Wyrd cannot resolve something: suspend</h3>
            <p>
                An instance is <strong>suspended, needing attention</strong>{' '}
                when:
            </p>
            <ul>
                <li>
                    a step&apos;s retries are exhausted (a failure Wyrd cannot
                    classify: an outage or a bug; suspending never cancels an
                    order because of a bug, and a process may opt into
                    compensating automatically instead),
                </li>
                <li>an undo&apos;s retries are exhausted,</li>
                <li>the outcome command fails after its retries,</li>
                <li>its definition&apos;s structure changed.</li>
            </ul>
            <p>
                Suspending does not make the world consistent: the instance
                stays half-way. It makes the situation <strong>visible</strong>{' '}
                (status, reason, alert) and <strong>recoverable</strong>: an
                operator <strong>resumes</strong> it after a fix, or{' '}
                <strong>compensates</strong> it. Nothing is lost and nothing
                stays stuck in silence. <Status kind="planned" version="v0.3" />
            </p>
            <p>
                Today an exhausted undo ends as <code>Failed</code>, and
                exhausted forward retries compensate.
            </p>

            <h3>Statuses</h3>
            <div className={styles['doc-table']}>
                <table>
                    <thead>
                        <tr>
                            <th>Status</th>
                            <th>Meaning</th>
                        </tr>
                    </thead>
                    <tbody>
                        {STATUSES.map(([status, meaning]) => (
                            <tr key={status}>
                                <td>
                                    <code>{status}</code>
                                </td>
                                <td>{meaning}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
            <p>
                <code>Failed</code> becomes <code>Suspended</code> when the
                suspension rules above are implemented.
            </p>
            <CodeBlock
                code={LIFECYCLE}
                language="text"
                title="Life of an instance"
            />

            <h2>Delivery</h2>
            <RuleTable
                rows={[
                    {
                        rule: (
                            <>
                                Every message is handled in{' '}
                                <strong>one transaction</strong>: state, events,
                                instances, inbox, outbox, timers and read models
                                commit together or not at all.
                            </>
                        ),
                        status: 'implemented',
                        today: (
                            <>
                                in the in-memory host. Durable hosts (EF Core on
                                PostgreSQL and SQL Server) are on the roadmap
                                for v0.5.
                            </>
                        ),
                    },
                    {
                        rule: (
                            <>
                                Delivery is <strong>at least once</strong>;
                                decisions are <strong>effectively once</strong>:
                                the owner&apos;s inbox remembers every command
                                it decided and replays the outcome to
                                duplicates.
                            </>
                        ),
                        status: 'implemented',
                    },
                    {
                        rule: (
                            <>
                                Integration events reach each subscriber{' '}
                                <strong>
                                    once, in the contract version it understands
                                </strong>
                                : older versions are upcast step by step, and a
                                newer version is never downcast.
                            </>
                        ),
                        status: 'implemented',
                    },
                ]}
            />

            <h2>Read models</h2>
            <RuleTable
                rows={[
                    {
                        rule: (
                            <>
                                A projection updates a view from events with
                                pure functions, in the transaction that handles
                                the event. The same message is never applied
                                twice.
                            </>
                        ),
                        status: 'implemented',
                    },
                    {
                        rule: (
                            <>
                                Events of one stream (one state key) reach a
                                projection{' '}
                                <strong>in the order they happened</strong>,
                                whatever the delivery order, so views do not
                                defend against reordering by hand.
                            </>
                        ),
                        status: 'planned',
                        version: 'v0.3',
                    },
                ]}
            />

            <h2>Conscious limits</h2>
            <p>
                Each of these is a choice with a stated reason and a way out,
                not an oversight.
            </p>
            <div className={styles['doc-table']}>
                <table>
                    <thead>
                        <tr>
                            <th>Limit</th>
                            <th>Why</th>
                            <th>Way out</th>
                        </tr>
                    </thead>
                    <tbody>
                        {LIMITS.map(([limit, why, out]) => (
                            <tr key={limit}>
                                <td>{limit}</td>
                                <td>{why}</td>
                                <td>{out}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
            <Callout title="Why each rule exists">
                The reasoning behind the main choices, with what each one gives
                you and what it costs, is on the{' '}
                <a href="#/docs/decisions">Design decisions</a> page.
            </Callout>
        </DocArticle>
    )
}
