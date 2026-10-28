import { useEffect, useId, useState } from 'react'
import { Box, Cloud, Cog, Database, Globe, Workflow } from 'lucide-react'
import css from './verdandi.module.css'

export type Kind = 'code' | 'wyrd' | 'db' | 'external'

export interface Node {
    id: string
    kind: Kind
    x: number
    y: number
    w: number
    h: number
    title: string
    sub: string
}

export const KINDS: { kind: Kind; label: string }[] = [
    { kind: 'code', label: 'Your code' },
    { kind: 'wyrd', label: 'Wyrd' },
    { kind: 'db', label: 'Database' },
    { kind: 'external', label: 'Outside' },
]

/** guides/how-verdandi-works.md, "The pieces" and "Where the data lives". */
export const NODES: Node[] = [
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

export interface Edge {
    id: string
    d: string
    label: string
    lx: number
    ly: number
    dashed?: boolean
}

export const EDGES: Edge[] = [
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

/** What a step lights up on the architecture: node and edge ids. */
export interface Path {
    nodes: string[]
    edges: string[]
}

interface CanvasProps {
    /** Everything lit so far; the rest fades. Nothing lit: the whole architecture, as is. */
    lit?: Path
    /** The step being shown now: its edges carry a moving message. */
    current?: Path
    /** Nodes in trouble now: a worker that died, a step that failed. */
    alert?: string[]
    /** Step numbers on edges, by edge id. */
    numbers?: Map<string, number>
    label?: string
}

/** The architecture of guides/how-verdandi-works.md ("The pieces"), drawn once and lit by every case on the page. */
/** The canvas inside a playground, lit by its current step. */
export function LiveArchitecture({
    path,
    alert,
    label,
}: {
    path?: Path
    alert?: string[]
    label: string
}) {
    return (
        <div className={css.liveArch}>
            <ArchitectureCanvas
                lit={path}
                current={path}
                alert={alert}
                label={label}
            />
        </div>
    )
}

export function ArchitectureCanvas({
    lit,
    current,
    alert: alerted = [],
    numbers = new Map(),
    label,
}: CanvasProps) {
    const id = useId().replace(/:/g, '')
    const still = useReducedMotion()
    const active = lit !== undefined
    const litNodes = new Set(lit?.nodes ?? [])
    const litEdges = new Set(lit?.edges ?? [])
    const currentNodes = new Set(current?.nodes ?? [])
    const currentEdges = new Set(current?.edges ?? [])
    const alert = new Set(alerted)

    return (
        <svg
            viewBox="0 0 1000 600"
            role="img"
            aria-label={
                label ??
                "Verdandi's architecture: your process with the runtime, the engine and workers, your database with Wyrd's tables, and the services and systems outside"
            }
        >
            <defs>
                <pattern
                    id={`${id}-dots`}
                    width="16"
                    height="16"
                    patternUnits="userSpaceOnUse"
                >
                    <circle cx="1" cy="1" r="1" className={css.dot} />
                </pattern>
                <marker
                    id={`${id}-arrow`}
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
                    id={`${id}-arrow-lit`}
                    viewBox="0 0 10 10"
                    refX="9"
                    refY="5"
                    markerWidth="7"
                    markerHeight="7"
                    orient="auto-start-reverse"
                >
                    <path d="M0 0 L10 5 L0 10 Z" className={css.archArrowLit} />
                </marker>
            </defs>
            <rect width="1000" height="600" fill={`url(#${id}-dots)`} />

            <g className={active ? css.dimmed : ''}>
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
                            active && !lit && css.faded,
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
                                    ? `url(#${id}-arrow-lit)`
                                    : `url(#${id}-arrow)`
                            }
                        />
                        <text x={e.lx} y={e.ly} className={css.edgeLabel}>
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
                            active && !lit && css.faded,
                            currentNodes.has(n.id) && css.nodeNow,
                            alert.has(n.id) && css.nodeAlert,
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
                            x={n.x + n.w / 2 + (n.kind === 'db' ? 0 : 8)}
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
            {!still &&
                EDGES.filter((e) => currentEdges.has(e.id)).map((e) => (
                    <circle key={`packet-${e.id}`} r="6" className={css.packet}>
                        <animateMotion
                            dur="1.3s"
                            repeatCount="indefinite"
                            path={e.d}
                        />
                    </circle>
                ))}
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
    )
}

/** True when the reader asked for less motion; false while prerendering. */
function useReducedMotion() {
    const [reduced, setReduced] = useState(false)
    useEffect(() => {
        const query = window.matchMedia('(prefers-reduced-motion: reduce)')
        setReduced(query.matches)
        const change = () => setReduced(query.matches)
        query.addEventListener('change', change)
        return () => query.removeEventListener('change', change)
    }, [])
    return reduced
}
