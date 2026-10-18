import styles from '../../styles/site.module.css'
import { Callout } from '../../components/common/Callout'
import { DocArticle } from './DocArticle'

type Item = readonly [done: boolean, text: string]

interface Milestone {
    version: string
    title: string
    status: string
    copy: string
    items: readonly Item[]
}

const ROADMAP: readonly Milestone[] = [
    {
        version: 'v0.0',
        title: 'Spike',
        status: 'Available',
        copy: 'The process model as data, with two syntaxes building the same model.',
        items: [
            [
                true,
                'Process model as data (Process, StepNode, AwaitNode) with structural equality',
            ],
            [true, 'Fluent syntax, and the opt-in pipe syntax (>>)'],
            [true, 'Extension member literals such as 15.Minutes and 3.Times'],
            [
                true,
                'Result<TOk, TError>, the [Union] marker, Mermaid rendering',
            ],
        ],
    },
    {
        version: 'v0.0.x',
        title: 'Runnable core',
        status: 'Available',
        copy: 'Slices with pure decisions and a deterministic in-memory runtime.',
        items: [
            [true, 'Slices with a static pure Decide, and state via IState'],
            [true, 'Explicit correlation with StartOn and Await'],
            [
                true,
                'Deterministic InMemoryRuntime with a virtual clock: retries, reverse compensation, timeouts, duplicates, correlation',
            ],
            [
                true,
                'samples/Shop and library scaffolding (CI on 3 operating systems)',
            ],
        ],
    },
    {
        version: 'v0.1',
        title: 'Messages done right',
        status: 'Available',
        copy: 'Commands, domain events and integration events as different things, enforced at build time.',
        items: [
            [
                true,
                'Message taxonomy, contracts split from slices, explicit publication mapping',
            ],
            [
                true,
                'Runtime rules: one owner per command, only integration events published from outside',
            ],
            [
                true,
                'Analyzers WYRD0001 to WYRD0004, contract snapshots with breaking-change detection (WYRD0200 to WYRD0204) and a code fix',
            ],
            [true, 'Multi-version publishing and upcasters for subscribers'],
        ],
    },
    {
        version: 'v0.2',
        title: 'Distributed-shaped core, in memory',
        status: 'Available',
        copy: 'A message-driven engine that behaves like a distributed system while staying deterministic.',
        items: [
            [
                true,
                'Steps as command messages with Accepted / Rejected / Faulted replies; everything crosses the queue as JSON',
            ],
            [
                true,
                'One transaction per message (state, inbox, outbox, timers) behind a storage port, with an in-memory store',
            ],
            [
                true,
                'Retries with backoff, early events parked, projections with redelivery protection',
            ],
            [
                true,
                'Process outcomes as commands (.OnCompensated), Wyrd.Testing Given/When/Then',
            ],
            [
                true,
                'Source-generated JSON with analyzers WYRD0300 to WYRD0302, and Native AOT checked in CI',
            ],
        ],
    },
    {
        version: 'v0.3',
        title: 'Skuld — simulate the future',
        status: 'Available',
        copy: 'Failure simulation with shrinking, explicit retry semantics, suspension, the structure fingerprint, steps that read earlier events and ordered read models.',
        items: [
            [
                true,
                'Skuld.SimulateAsync: seeded futures with faults, redelivery and reordering, invariants, replay by seed',
            ],
            [
                true,
                'Engine fixes found by Skuld: attempt-aware replies, unknown-outcome steps compensated, thread-safe type registry',
            ],
            [
                true,
                'Typed invokers in the pipe syntax; only steps that read earlier events are invoked dynamically (Native AOT-compatible)',
            ],
            [
                true,
                'Analyzers WYRD0011 (how a step is undone), WYRD0012 (await without .Within), WYRD0013 (process style), WYRD0020 (exhaustive union switch)',
            ],
            [
                true,
                'Retry semantics: every step declares .Retry(...) or .NoRetry("why") (WYRD0014); undos and the outcome always insist; WYRD0010 removed',
            ],
            [
                true,
                'Suspend instead of guessing, with ResumeAsync / CompensateAsync and opt-in .OrCompensate()',
            ],
            [
                true,
                'Process structure fingerprint per instance: a changed definition suspends in-flight instances',
            ],
            [
                true,
                'Steps that read the events of earlier steps (and awaited events), checked before the first message',
            ],
            [true, 'Ordered projections per stream'],
            [
                true,
                'Shrinking: reduce a failing future to the fewest perturbations that still break the invariant',
            ],
        ],
    },
    {
        version: 'v0.4',
        title: 'Urd — replay the past, and the dotnet wyrd tool',
        status: 'Available',
        copy: 'Capture in-memory instance histories and replay them against today’s process definition with Urd.',
        items: [
            [true, 'dotnet wyrd asyncapi'],
            [true, 'dotnet wyrd diagram'],
            [true, 'Process instance history capture'],
            [true, 'dotnet wyrd replay <history.json> --as-test'],
        ],
    },
    {
        version: 'v0.5',
        title: 'Verdandi — run the present',
        status: 'Planned',
        copy: 'The durable runtime and the only place with real I/O.',
        items: [
            [
                false,
                'Integrations (IIntegration): the only I/O, with idempotency keys and recorded outcomes',
            ],
            [
                false,
                'Framework reply envelope and re-send with stable message ids',
            ],
            [false, 'Ports for transport, outbox, process state and scheduler'],
            [false, 'EF Core outbox, inbox and state (PostgreSQL, SQL Server)'],
            [false, 'Durable scheduler for timeouts'],
            [false, 'RabbitMQ transport'],
            [false, 'OpenTelemetry with process-level traces'],
        ],
    },
    {
        version: 'Docs',
        title: 'Documentation & adoption',
        status: 'Continuous',
        copy: 'Teaching the mental model matters as much as the code.',
        items: [
            [true, 'Guide: From classic DDD to Wyrd'],
            [
                true,
                'Guidance on shared invariants living on the state or in pure domain policies',
            ],
            [false, 'Rich aggregate in samples/Shop (Order, OrderLine, Money)'],
            [false, 'Guide: modelling read models for screens and reports'],
            [
                false,
                'Docs site with the mental model first, API reference second',
            ],
        ],
    },
    {
        version: 'v1.0',
        title: 'Production ready',
        status: 'Planned',
        copy: 'Everything needed to run Wyrd with confidence.',
        items: [
            [
                false,
                'Process versioning: a snapshot of every process, with breaking changes failing the build until in-flight instances are migrated',
            ],
            [false, 'Native AOT verified end to end'],
            [false, 'dotnet wyrd CLI and an MCP server for agents'],
            [false, 'Docs site'],
        ],
    },
] as const

export function RoadmapDoc() {
    return (
        <DocArticle
            eyebrow="Direction"
            title="Roadmap"
            lead="Wyrd grows around three capabilities: simulate the future, replay the past, and run the present."
        >
            <div className={styles['roadmap-list']}>
                {ROADMAP.map(({ version, title, status, copy, items }) => (
                    <article key={version}>
                        <span>{version}</span>
                        <div>
                            <div className={styles['roadmap-title']}>
                                <h2>{title}</h2>
                                <em>{status}</em>
                            </div>
                            <p>{copy}</p>
                            <ul className={styles['roadmap-items']}>
                                {items.map(([done, text]) => (
                                    <li
                                        key={text}
                                        className={done ? styles.done : ''}
                                    >
                                        <span>{done ? 'Done' : 'Open'}</span>
                                        <div>{text}</div>
                                    </li>
                                ))}
                            </ul>
                        </div>
                    </article>
                ))}
            </div>
            <h2>Later</h2>
            <ul>
                <li>Event Modeling to code and back</li>
                <li>Live process dashboard</li>
                <li>
                    Azure Service Bus and Kafka transports, and community
                    adapters (Wolverine, MassTransit)
                </li>
            </ul>
            <h2>Open decisions</h2>
            <ul>
                <li>State persistence: state-stored, event-sourced, or both</li>
                <li>
                    The full rules catalog (what &quot;correct&quot; means, rule
                    by rule)
                </li>
            </ul>
            <Callout title="The roadmap is a living contract">
                The repository roadmap is the source of truth. Items marked Open
                here are not claims about the current package. What is available
                behaves as specified in{' '}
                <a href="#/docs/semantics">How Wyrd behaves</a>.
            </Callout>
        </DocArticle>
    )
}
