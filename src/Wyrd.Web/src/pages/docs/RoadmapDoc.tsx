import styles from '../../styles/site.module.css'
import { Callout } from '../../components/common/Callout'
import { DocArticle } from './DocArticle'

const ROADMAP = [
    [
        'v0.2',
        'Distributed-shaped core',
        'Available',
        'Message-driven engine, atomic change sets, virtual time, projections, source-generated JSON, and Native AOT validation.',
    ],
    [
        'v0.3',
        'Skuld — simulation',
        'In progress',
        'Skuld simulation is available: seeded hostile futures, invariants, exact replay, and the engine fixes it found, such as fencing for unknown outcomes. Next: an analyzer for undos without retries, ordered projections, state-aware steps, and typed invokers.',
    ],
    [
        'v0.4',
        'Urd — replay',
        'In progress',
        'The documentation tool already generates AsyncAPI and Mermaid. Instance capture and deterministic replay come next.',
    ],
    [
        'v0.5',
        'Verdandi — durable runtime',
        'Planned',
        'I/O integrations, durable state and scheduling, outbox/inbox storage, transports, and OpenTelemetry.',
    ],
    [
        'v1.0',
        'Production ready',
        'Planned',
        'Process versioning, end-to-end Native AOT, agent tooling, and complete framework documentation.',
    ],
] as const

export function RoadmapDoc() {
    return (
        <DocArticle
            eyebrow="Direction"
            title="Roadmap"
            lead="Wyrd grows around three capabilities: simulate the future, replay the past, and run the present."
        >
            <div className={styles['roadmap-list']}>
                {ROADMAP.map(([version, title, status, copy]) => (
                    <article key={version}>
                        <span>{version}</span>
                        <div>
                            <div className={styles['roadmap-title']}>
                                <h2>{title}</h2>
                                <em>{status}</em>
                            </div>
                            <p>{copy}</p>
                        </div>
                    </article>
                ))}
            </div>
            <Callout title="The roadmap is a living contract">
                The repository roadmap is the source of truth. Features marked
                as planned here are not claims about the current package.
            </Callout>
        </DocArticle>
    )
}
