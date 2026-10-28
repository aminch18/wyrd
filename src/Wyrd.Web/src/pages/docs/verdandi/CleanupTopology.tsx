import { Status } from '../Status'
import css from './verdandi.module.css'

const VW = 360
const POD = 112
const GAP = 8
/** Left edge of replica i, the three of them centred. */
const replicaX = (i: number) => (VW - 3 * POD - 2 * GAP) / 2 + i * (POD + GAP)
const VH = 250

function Pod({
    x,
    y,
    w,
    title,
    sub,
    dashed,
}: {
    x: number
    y: number
    w: number
    title: string
    sub: string
    dashed?: boolean
}) {
    return (
        <g>
            <rect
                x={x}
                y={y}
                width={w}
                height={46}
                rx={8}
                className={dashed ? css.podDashed : css.pod}
            />
            <text x={x + w / 2} y={y + 20} className={css.podTitle}>
                {title}
            </text>
            <text x={x + w / 2} y={y + 36} className={css.podSub}>
                {sub}
            </text>
        </g>
    )
}

function Database({ y }: { y: number }) {
    return (
        <g>
            <rect
                x={(VW - 180) / 2}
                y={y}
                width={180}
                height={44}
                rx={10}
                className={css.db}
            />
            <text x={VW / 2} y={y + 20} className={css.podTitle}>
                your database
            </text>
            <text x={VW / 2} y={y + 35} className={css.podSub}>
                wyrd.* tables
            </text>
        </g>
    )
}

function Arrow({
    x1,
    y1,
    x2,
    y2,
}: {
    x1: number
    y1: number
    x2: number
    y2: number
}) {
    return (
        <line
            x1={x1}
            y1={y1}
            x2={x2}
            y2={y2}
            className={css.arrow}
            markerEnd="url(#cleanup-arrow)"
        />
    )
}

function Defs() {
    return (
        <defs>
            <marker
                id="cleanup-arrow"
                viewBox="0 0 10 10"
                refX="9"
                refY="5"
                markerWidth="7"
                markerHeight="7"
                orient="auto-start-reverse"
            >
                <path d="M0 0 L10 5 L0 10 Z" className={css.arrowHead} />
            </marker>
        </defs>
    )
}

export function CleanupTopology() {
    return (
        <figure className={css.figure}>
            <div className={css.topology}>
                <section>
                    <h4>
                        <code>CleanupMode.Workers</code>{' '}
                        <Status kind="implemented" />
                    </h4>
                    <svg
                        viewBox={`0 0 ${VW} ${VH}`}
                        role="img"
                        aria-label="Three API replicas, each with workers that also clean, writing to one database"
                    >
                        <Defs />
                        <text x={VW / 2} y={18} className={css.groupLabel}>
                            API Deployment · 3 replicas
                        </text>
                        {[0, 1, 2].map((i) => (
                            <g key={i}>
                                <Pod
                                    x={replicaX(i)}
                                    y={30}
                                    w={POD}
                                    title={`replica ${i + 1}`}
                                    sub="workers + cleanup"
                                />
                                <Arrow
                                    x1={replicaX(i) + POD / 2}
                                    y1={78}
                                    x2={VW / 2 + (i - 1) * 50}
                                    y2={116}
                                />
                            </g>
                        ))}
                        <Database y={120} />
                    </svg>
                    <p>
                        The default. Every <code>CleanupInterval</code> (one
                        minute by default), each process’s workers remove a
                        bounded amount between messages. Simple, and right for
                        one instance or a few.
                    </p>
                </section>
                <section>
                    <h4>
                        <code>CleanupMode.Separate</code>{' '}
                        <Status kind="planned" />
                    </h4>
                    <svg
                        viewBox={`0 0 ${VW} ${VH}`}
                        role="img"
                        aria-label="Three API replicas with workers only, and one cleaner, a one-replica Deployment or a CronJob, writing to one database"
                    >
                        <Defs />
                        <text x={VW / 2} y={18} className={css.groupLabel}>
                            API Deployment · 3 replicas
                        </text>
                        {[0, 1, 2].map((i) => (
                            <g key={i}>
                                <Pod
                                    x={replicaX(i)}
                                    y={30}
                                    w={POD}
                                    title={`replica ${i + 1}`}
                                    sub="workers only"
                                />
                                <Arrow
                                    x1={replicaX(i) + POD / 2}
                                    y1={78}
                                    x2={VW / 2 + (i - 1) * 50}
                                    y2={116}
                                />
                            </g>
                        ))}
                        <Pod
                            x={20}
                            y={196}
                            w={140}
                            title="cleaner · 1 replica"
                            sub="RunCleanupAsync"
                            dashed
                        />
                        <text x={VW / 2} y={223} className={css.podSub}>
                            or
                        </text>
                        <Pod
                            x={VW - 160}
                            y={196}
                            w={140}
                            title="CronJob"
                            sub="ForgetExpiredAsync"
                            dashed
                        />
                        <Arrow x1={90} y1={194} x2={VW / 2 - 40} y2={168} />
                        <Arrow
                            x1={VW - 90}
                            y1={194}
                            x2={VW / 2 + 40}
                            y2={168}
                        />
                        <Database y={120} />
                    </svg>
                    <p>
                        Workers only handle messages, and one cleaner removes
                        what expired, so many API replicas don’t all clean the
                        same tables at once. It comes with pull request #62 and
                        is not in a release yet.
                    </p>
                </section>
            </div>
            <figcaption className={css.caption}>
                <strong>The cleaner is configured like the workers:</strong> the
                same retention, transport and history archive. What it removes
                depends on them.
            </figcaption>
        </figure>
    )
}
