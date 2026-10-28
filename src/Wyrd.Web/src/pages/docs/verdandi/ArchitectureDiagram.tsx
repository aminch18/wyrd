import { useEffect, useState, type ReactNode } from 'react'
import { Box, Cloud, Cog, Database, Globe, Workflow } from 'lucide-react'
import css from './verdandi.module.css'

type Kind = 'code' | 'wyrd' | 'db' | 'external'

interface Node {
    id: string
    kind: Kind
    x: number
    y: number
    w: number
    h: number
    title: string
    sub: string
}

const KINDS: { kind: Kind; label: string }[] = [
    { kind: 'code', label: 'Your code' },
    { kind: 'wyrd', label: 'Wyrd' },
    { kind: 'db', label: 'Database' },
    { kind: 'external', label: 'Outside' },
]

/** guides/how-verdandi-works.md, "The pieces" and "Where the data lives". */
const NODES: Node[] = [
    {
        id: 'clients',
        kind: 'external',
        x: 40,
        y: 14,
        w: 190,
        h: 60,
        title: 'Clients',
        sub: 'HTTP requests, schedules',
    },
    {
        id: 'endpoints',
        kind: 'code',
        x: 40,
        y: 150,
        w: 190,
        h: 70,
        title: 'Endpoints & jobs',
        sub: 'your API, your handlers',
    },
    {
        id: 'runtime',
        kind: 'wyrd',
        x: 300,
        y: 150,
        w: 190,
        h: 70,
        title: 'EfCoreRuntime',
        sub: 'a singleton, no state',
    },
    {
        id: 'engine',
        kind: 'wyrd',
        x: 560,
        y: 150,
        w: 180,
        h: 70,
        title: 'Engine',
        sub: 'your pure deciders',
    },
    {
        id: 'cleanup',
        kind: 'wyrd',
        x: 40,
        y: 262,
        w: 190,
        h: 62,
        title: 'Cleanup',
        sub: 'in the workers',
    },
    {
        id: 'workers',
        kind: 'wyrd',
        x: 560,
        y: 262,
        w: 180,
        h: 62,
        title: 'Workers',
        sub: 'RunAsync, many of them',
    },
    {
        id: 'services',
        kind: 'external',
        x: 800,
        y: 150,
        w: 180,
        h: 70,
        title: 'Other services',
        sub: 'IWyrdTransport',
    },
    {
        id: 'systems',
        kind: 'external',
        x: 800,
        y: 262,
        w: 180,
        h: 62,
        title: 'Other systems',
        sub: 'integrations',
    },
    {
        id: 'states',
        kind: 'db',
        x: 40,
        y: 430,
        w: 140,
        h: 56,
        title: 'states',
        sub: 'your data',
    },
    {
        id: 'views',
        kind: 'db',
        x: 196,
        y: 430,
        w: 140,
        h: 56,
        title: 'views',
        sub: 'your read models',
    },
    {
        id: 'queue',
        kind: 'db',
        x: 352,
        y: 430,
        w: 140,
        h: 56,
        title: 'queue',
        sub: 'due messages, timers',
    },
    {
        id: 'inbox',
        kind: 'db',
        x: 508,
        y: 430,
        w: 140,
        h: 56,
        title: 'inbox · handled',
        sub: 'duplicates',
    },
    {
        id: 'instances',
        kind: 'db',
        x: 664,
        y: 430,
        w: 140,
        h: 56,
        title: 'instances · parked',
        sub: 'processes',
    },
    {
        id: 'events',
        kind: 'db',
        x: 820,
        y: 430,
        w: 140,
        h: 56,
        title: 'events',
        sub: 'what happened',
    },
    {
        id: 'published',
        kind: 'db',
        x: 352,
        y: 510,
        w: 140,
        h: 56,
        title: 'published',
        sub: 'the outbox',
    },
    {
        id: 'dead',
        kind: 'db',
        x: 508,
        y: 510,
        w: 140,
        h: 56,
        title: 'dead_letters',
        sub: 'gave up',
    },
]

interface Edge {
    id: string
    d: string
    label: string
    lx: number
    ly: number
    dashed?: boolean
}

const EDGES: Edge[] = [
    { id: 'http', d: 'M135 74 V150', label: 'HTTP', lx: 135, ly: 112 },
    { id: 'send', d: 'M230 185 H300', label: 'SendAsync', lx: 265, ly: 176 },
    { id: 'decide', d: 'M490 185 H560', label: 'decide', lx: 525, ly: 176 },
    {
        id: 'tx',
        d: 'M395 220 V400',
        label: 'one transaction',
        lx: 395,
        ly: 362,
    },
    {
        id: 'workerDecide',
        d: 'M650 262 V220',
        label: 'decide',
        lx: 674,
        ly: 244,
    },
    {
        id: 'take',
        d: 'M610 324 V400',
        label: 'take · write · commit',
        lx: 610,
        ly: 362,
    },
    {
        id: 'clean',
        d: 'M135 324 V400',
        label: 'delete what expired',
        lx: 135,
        ly: 362,
    },
    {
        id: 'relay',
        d: 'M740 280 H770 V185 H800',
        label: 'relay',
        lx: 770,
        ly: 232,
    },
    { id: 'call', d: 'M740 300 H800', label: 'call', lx: 770, ly: 292 },
    {
        id: 'receive',
        d: 'M890 150 V104 H395 V150',
        label: 'ReceiveAsync · PublishAsync',
        lx: 640,
        ly: 96,
        dashed: true,
    },
]

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

const ICONS: Record<Kind, typeof Box> = {
    code: Box,
    wyrd: Cog,
    db: Database,
    external: Globe,
}
const NODE_ICONS: Record<string, typeof Box> = {
    services: Cloud,
    systems: Workflow,
}

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
    const currentEdges = new Set(current?.edges ?? [])
    const currentNodes = new Set(current?.nodes ?? [])
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
                <svg
                    viewBox="0 0 1000 600"
                    role="img"
                    aria-label="Verdandi's architecture: your process with the runtime, the engine and workers, your database with Wyrd's tables, and the services and systems outside"
                >
                    <defs>
                        <pattern
                            id="arch-dots"
                            width="16"
                            height="16"
                            patternUnits="userSpaceOnUse"
                        >
                            <circle cx="1" cy="1" r="1" className={css.dot} />
                        </pattern>
                        <marker
                            id="arch-arrow"
                            viewBox="0 0 10 10"
                            refX="9"
                            refY="5"
                            markerWidth="7"
                            markerHeight="7"
                            orient="auto-start-reverse"
                        >
                            <path
                                d="M0 0 L10 5 L0 10 Z"
                                className={css.archArrowHead}
                            />
                        </marker>
                        <marker
                            id="arch-arrow-lit"
                            viewBox="0 0 10 10"
                            refX="9"
                            refY="5"
                            markerWidth="7"
                            markerHeight="7"
                            orient="auto-start-reverse"
                        >
                            <path
                                d="M0 0 L10 5 L0 10 Z"
                                className={css.archArrowLit}
                            />
                        </marker>
                    </defs>
                    <rect width="1000" height="600" fill="url(#arch-dots)" />

                    <g className={scenario ? css.dimmed : ''}>
                        <rect
                            x="20"
                            y="120"
                            width="740"
                            height="222"
                            rx="14"
                            className={css.groupProcess}
                        />
                        <text
                            x="748"
                            y="139"
                            textAnchor="end"
                            className={css.groupProcessLabel}
                        >
                            your process · one per instance
                        </text>
                        <rect
                            x="20"
                            y="400"
                            width="960"
                            height="184"
                            rx="14"
                            className={css.groupDb}
                        />
                        <text x="34" y="420" className={css.groupDbLabel}>
                            your database · the wyrd schema, in your DbContext
                        </text>
                    </g>

                    {EDGES.map((e) => {
                        const lit = litEdges.has(e.id)
                        const now = currentEdges.has(e.id)
                        return (
                            <g
                                key={e.id}
                                className={[
                                    css.edge,
                                    scenario && !lit && css.faded,
                                    lit && css.edgeLit,
                                    now && css.edgeNow,
                                ]
                                    .filter(Boolean)
                                    .join(' ')}
                            >
                                <path
                                    d={e.d}
                                    className={e.dashed ? css.edgeDashed : ''}
                                    markerEnd={
                                        lit
                                            ? 'url(#arch-arrow-lit)'
                                            : 'url(#arch-arrow)'
                                    }
                                />
                                <text
                                    x={e.lx}
                                    y={e.ly}
                                    className={css.edgeLabel}
                                >
                                    {e.label}
                                </text>
                            </g>
                        )
                    })}

                    {NODES.map((n) => {
                        const Icon = NODE_ICONS[n.id] ?? ICONS[n.kind]
                        const lit = litNodes.has(n.id)
                        return (
                            <g
                                key={n.id}
                                className={[
                                    css.node,
                                    css[`kind-${n.kind}`],
                                    scenario && !lit && css.faded,
                                    currentNodes.has(n.id) && css.nodeNow,
                                ]
                                    .filter(Boolean)
                                    .join(' ')}
                            >
                                <rect
                                    x={n.x}
                                    y={n.y}
                                    width={n.w}
                                    height={n.h}
                                    rx="10"
                                />
                                {n.kind !== 'db' && (
                                    <Icon
                                        x={n.x + 10}
                                        y={n.y + 9}
                                        width={14}
                                        height={14}
                                        className={css.nodeIcon}
                                    />
                                )}
                                <text
                                    x={
                                        n.x +
                                        n.w / 2 +
                                        (n.kind === 'db' ? 0 : 8)
                                    }
                                    y={n.y + n.h / 2 - 2}
                                    className={css.nodeTitle}
                                >
                                    {n.title}
                                </text>
                                <text
                                    x={n.x + n.w / 2}
                                    y={n.y + n.h / 2 + 15}
                                    className={css.nodeSub}
                                >
                                    {n.sub}
                                </text>
                            </g>
                        )
                    })}
                    {EDGES.filter((e) => numbers.has(e.id)).map((e) => (
                        <g
                            key={`badge-${e.id}`}
                            className={css.badge}
                            transform={`translate(${e.lx} ${e.ly - 19})`}
                        >
                            <circle r="9" />
                            <text y="3.5">{numbers.get(e.id)}</text>
                        </g>
                    ))}
                </svg>
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
