import { useEffect, useState, type ReactNode } from 'react'
import {
    ArchitectureCanvas,
    KINDS,
    NODES,
    type Kind,
} from './ArchitectureCanvas'
import css from './verdandi.module.css'

interface Step {
    edges: string[]
    nodes: string[]
    text: ReactNode
}

interface Scenario {
    id: string
    title: string
    steps: Step[]
}

/** Each scenario is what the guide says, drawn on the diagram. */
const SCENARIOS: Scenario[] = [
    {
        id: 'command',
        title: 'A command from your API',
        steps: [
            {
                edges: ['http'],
                nodes: ['clients', 'endpoints'],
                text: <>A request reaches your endpoint.</>,
            },
            {
                edges: ['send'],
                nodes: ['endpoints', 'runtime'],
                text: (
                    <>
                        <code>SendAsync(PlaceOrder)</code>: the runtime opens
                        its own context and transaction.
                    </>
                ),
            },
            {
                edges: ['decide'],
                nodes: ['runtime', 'engine'],
                text: <>The engine calls your pure decider.</>,
            },
            {
                edges: ['tx'],
                nodes: ['runtime', 'inbox', 'states', 'events', 'queue'],
                text: (
                    <>
                        One transaction: the inbox (seen this id?), the state
                        with its version, the event, and what it causes, queued.
                        Then the decision goes back to the caller.
                    </>
                ),
            },
        ],
    },
    {
        id: 'worker',
        title: 'A worker handles a message',
        steps: [
            {
                edges: ['take'],
                nodes: ['workers', 'queue'],
                text: (
                    <>
                        A worker takes the next due row of <code>queue</code>,{' '}
                        <code>SKIP LOCKED</code>: other workers pass over it.
                    </>
                ),
            },
            {
                edges: ['take'],
                nodes: ['workers', 'inbox'],
                text: (
                    <>
                        A duplicate is answered (a command) or ignored (an
                        event).
                    </>
                ),
            },
            {
                edges: ['workerDecide'],
                nodes: ['workers', 'engine'],
                text: <>The engine decides.</>,
            },
            {
                edges: ['take'],
                nodes: [
                    'workers',
                    'states',
                    'events',
                    'queue',
                    'views',
                    'instances',
                    'inbox',
                ],
                text: (
                    <>
                        Everything is written and the row deleted, in the same
                        transaction: all of it commits, or none of it.
                    </>
                ),
            },
        ],
    },
    {
        id: 'out',
        title: 'Events to other services',
        steps: [
            {
                edges: ['take'],
                nodes: ['workers', 'published'],
                text: (
                    <>
                        An integration event is written to{' '}
                        <code>published</code> in the same transaction as the
                        state it comes from: the outbox.
                    </>
                ),
            },
            {
                edges: ['take', 'relay'],
                nodes: ['published', 'workers', 'services'],
                text: (
                    <>
                        Workers relay it through the transport (RabbitMQ, say).
                        One sent but not yet marked is sent again; the receiver
                        discards the duplicate.
                    </>
                ),
            },
        ],
    },
    {
        id: 'in',
        title: 'Events from other services',
        steps: [
            {
                edges: ['receive'],
                nodes: ['services', 'runtime'],
                text: (
                    <>
                        A message from another service, or a fact you publish,
                        reaches the runtime.
                    </>
                ),
            },
            {
                edges: ['tx'],
                nodes: ['runtime', 'queue', 'inbox'],
                text: (
                    <>
                        It is queued; a duplicate is recognised by its id in{' '}
                        <code>handled</code>.
                    </>
                ),
            },
        ],
    },
    {
        id: 'integration',
        title: 'A call to another system',
        steps: [
            {
                edges: ['take'],
                nodes: ['workers', 'queue'],
                text: (
                    <>
                        The worker takes a lease on the integration command; no
                        transaction is open.
                    </>
                ),
            },
            {
                edges: ['call'],
                nodes: ['workers', 'systems'],
                text: (
                    <>
                        It calls the other system with an idempotency key, the
                        same on every attempt.
                    </>
                ),
            },
            {
                edges: ['take'],
                nodes: ['workers', 'states', 'events', 'queue', 'inbox'],
                text: (
                    <>
                        The outcome is recorded in a second transaction. A
                        worker that stopped mid-call leaves it to another once
                        the lease runs out.
                    </>
                ),
            },
        ],
    },
    {
        id: 'timer',
        title: 'A timer',
        steps: [
            {
                edges: ['take'],
                nodes: ['workers', 'queue', 'instances'],
                text: (
                    <>
                        A process waits: its timeout is a <code>queue</code>{' '}
                        row, due at the deadline.
                    </>
                ),
            },
            {
                edges: ['take'],
                nodes: ['workers', 'queue'],
                text: (
                    <>
                        The awaited event arrives first: the same transaction
                        deletes the row. The deadline comes first: a worker
                        takes the row like any message, and the process times
                        out.
                    </>
                ),
            },
        ],
    },
    {
        id: 'failure',
        title: 'A message that keeps failing',
        steps: [
            {
                edges: ['take'],
                nodes: ['workers', 'queue'],
                text: (
                    <>
                        A handler throws: the transaction rolls back and the row
                        is tried again later, with a growing delay.
                    </>
                ),
            },
            {
                edges: ['take'],
                nodes: ['workers', 'dead'],
                text: (
                    <>
                        After <code>MaxDeliveryAttempts</code> it moves to{' '}
                        <code>dead_letters</code>, for someone to look at.
                    </>
                ),
            },
        ],
    },
    {
        id: 'cleanup',
        title: 'Cleanup',
        steps: [
            {
                edges: [],
                nodes: ['workers', 'cleanup'],
                text: (
                    <>
                        Every <code>CleanupInterval</code> (a minute by
                        default), the workers clean, between messages.
                    </>
                ),
            },
            {
                edges: ['clean'],
                nodes: [
                    'cleanup',
                    'inbox',
                    'instances',
                    'events',
                    'published',
                    'dead',
                ],
                text: (
                    <>
                        What its retention no longer keeps goes, in short
                        batches. <code>states</code> and <code>views</code> are
                        your data: they stay.
                    </>
                ),
            },
        ],
    },
]

const CARDS = [
    {
        kind: 'code' as Kind,
        title: 'Your code',
        points: [
            'Pure deciders, processes and projections',
            'Endpoints call SendAsync and return its decision',
            'Never touches storage or the network',
        ],
    },
    {
        kind: 'wyrd' as Kind,
        title: 'The runtime',
        points: [
            'One per process, holds nothing between calls',
            'Every call: its own DbContext, its own transaction',
            'Workers: as many as you like, in one process or many',
        ],
    },
    {
        kind: 'db' as Kind,
        title: 'The database',
        points: [
            'The queue and the outbox: no broker inside a service',
            'Wyrd’s tables live in your DbContext (wyrd schema)',
            'Everything a message writes commits together',
        ],
    },
]

export function ArchitectureDiagram() {
    const [selected, setSelected] = useState<string | null>(null)
    const [preview, setPreview] = useState<string | null>(null)
    const [stepIndex, setStepIndex] = useState(0)
    // A case can be linked to: ?case=worker&step=2.
    useEffect(() => {
        const query = new URLSearchParams(window.location.search)
        const linked = SCENARIOS.find((s) => s.id === query.get('case'))
        if (!linked) return
        setSelected(linked.id)
        const step = Number(query.get('step') ?? 1) - 1
        setStepIndex(Math.min(Math.max(0, step), linked.steps.length - 1))
    }, [])
    const activeId = preview ?? selected
    const scenario = SCENARIOS.find((s) => s.id === activeId)
    const showing = scenario
        ? scenario.id === selected
            ? scenario.steps.slice(0, stepIndex + 1)
            : scenario.steps
        : []
    const litNodes = new Set(showing.flatMap((s) => s.nodes))
    const litEdges = new Set(showing.flatMap((s) => s.edges))
    const current =
        scenario && scenario.id === selected
            ? scenario.steps[stepIndex]
            : undefined
    // The first step an edge appears in is its number on the diagram.
    const numbers = new Map<string, number>()
    showing.forEach((s, i) =>
        s.edges.forEach((e) => numbers.has(e) || numbers.set(e, i + 1)),
    )

    const choose = (id: string) => {
        setSelected((s) => (s === id ? null : id))
        setStepIndex(0)
    }

    return (
        <div className={css.architecture}>
            <div className={css.archCanvas}>
                <ArchitectureCanvas
                    lit={
                        scenario
                            ? { nodes: [...litNodes], edges: [...litEdges] }
                            : undefined
                    }
                    current={current}
                    numbers={numbers}
                />
            </div>

            <div className={css.archLegend}>
                <div className={css.kinds}>
                    {KINDS.map((k) => (
                        <span
                            key={k.kind}
                            className={`${css.kindChip} ${css[`kind-${k.kind}`]}`}
                        >
                            <i /> {k.label}{' '}
                            <b>
                                {NODES.filter((n) => n.kind === k.kind).length}
                            </b>
                        </span>
                    ))}
                </div>
                <p className={css.legendHint}>
                    Pick a case to follow it through the architecture:
                </p>
                <div
                    className={css.scenarioChips}
                    role="group"
                    aria-label="Cases"
                >
                    {SCENARIOS.map((s) => (
                        <button
                            key={s.id}
                            type="button"
                            aria-pressed={s.id === selected}
                            onClick={() => choose(s.id)}
                            onMouseEnter={() => setPreview(s.id)}
                            onMouseLeave={() => setPreview(null)}
                            onFocus={() => setPreview(s.id)}
                            onBlur={() => setPreview(null)}
                        >
                            {s.title}
                        </button>
                    ))}
                </div>
                {scenario && scenario.id === selected && (
                    <div className={css.scenarioSteps} aria-live="polite">
                        <ol>
                            {scenario.steps.map((s, i) => (
                                <li
                                    key={i}
                                    className={
                                        i === stepIndex
                                            ? css.stepNow
                                            : i < stepIndex
                                              ? css.stepDone
                                              : css.stepLater
                                    }
                                >
                                    <button
                                        type="button"
                                        onClick={() => setStepIndex(i)}
                                    >
                                        <span className={css.stepNumber}>
                                            {i + 1}
                                        </span>
                                        <span>{s.text}</span>
                                    </button>
                                </li>
                            ))}
                        </ol>
                        <div className={css.controls}>
                            <button
                                type="button"
                                onClick={() =>
                                    setStepIndex((i) => Math.max(0, i - 1))
                                }
                                disabled={stepIndex === 0}
                            >
                                Previous
                            </button>
                            <button
                                type="button"
                                className={css.primary}
                                onClick={() =>
                                    setStepIndex((i) =>
                                        Math.min(
                                            scenario.steps.length - 1,
                                            i + 1,
                                        ),
                                    )
                                }
                                disabled={
                                    stepIndex === scenario.steps.length - 1
                                }
                            >
                                Next step
                            </button>
                        </div>
                    </div>
                )}
            </div>

            <div className={css.cards}>
                {CARDS.map((c) => (
                    <section
                        key={c.title}
                        className={`${css.card} ${css[`kind-${c.kind}`]}`}
                    >
                        <h4>
                            <i /> {c.title}
                        </h4>
                        <ul>
                            {c.points.map((p) => (
                                <li key={p}>{p}</li>
                            ))}
                        </ul>
                    </section>
                ))}
            </div>
        </div>
    )
}
