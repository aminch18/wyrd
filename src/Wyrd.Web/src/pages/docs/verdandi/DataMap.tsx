import { useState } from 'react'
import css from './verdandi.module.css'

interface Table {
    name: string
    holds: string
    keeps: string
    /** When the retention starts counting (semantics.md, 4.1). */
    from?: string
}

/** guides/how-verdandi-works.md, "Where the data lives", and semantics.md 4.1. */
const GROUPS: { title: string; note: string; tables: Table[] }[] = [
    {
        title: 'Business data',
        note: 'Grows with your business, like any table of yours.',
        tables: [
            {
                name: 'states',
                holds: 'The current state of each slice key (an order, a SKU), as JSON, with a version.',
                keeps: 'Always: it is your data.',
            },
            {
                name: 'views',
                holds: 'Read models, kept up to date by projections.',
                keeps: 'Always: it is your data.',
            },
        ],
    },
    {
        title: 'Delivery',
        note: 'Needed for a while; then the retention you chose removes it.',
        tables: [
            {
                name: 'queue',
                holds: 'Every message waiting to be handled: events, commands of processes, replies, timers, retries.',
                keeps: 'Until handled.',
            },
            {
                name: 'inbox',
                holds: 'The outcome of every command, by message id: a duplicate gets the same answer.',
                keeps: 'Deduplication',
                from: 'when it was handled',
            },
            {
                name: 'handled',
                holds: 'The id of every event handled: a duplicate is ignored.',
                keeps: 'Deduplication',
                from: 'when it was handled',
            },
            {
                name: 'instances',
                holds: 'The running and finished processes, with their history.',
                keeps: 'Finished ones: FinishedInstances. Running, waiting and suspended ones never expire.',
                from: 'when it finished',
            },
            {
                name: 'parked',
                holds: 'An event that arrived before its process was waiting for it.',
                keeps: 'With its instance.',
            },
            {
                name: 'events',
                holds: 'The domain events: the record of what happened.',
                keeps: 'Events',
                from: 'when recorded',
            },
        ],
    },
    {
        title: 'To other services',
        note: 'The outbox: written in the same transaction, relayed after.',
        tables: [
            {
                name: 'published',
                holds: 'Integration events for other services (the outbox).',
                keeps: 'RelayedMessages',
                from: 'when relayed (when published, on a host without a transport)',
            },
        ],
    },
    {
        title: 'Failures',
        note: 'For someone to look at.',
        tables: [
            {
                name: 'dead_letters',
                holds: 'Messages that gave up after their delivery attempts.',
                keeps: 'DeadLetters',
                from: 'when they failed',
            },
        ],
    },
]

const ALL = GROUPS.flatMap((g) => g.tables)
const isMember = (keeps: string) => /^[A-Z]\w+$/.test(keeps)

export function DataMap() {
    const [selected, setSelected] = useState('queue')
    const table = ALL.find((t) => t.name === selected) ?? ALL[0]

    return (
        <figure className={css.figure}>
            <div className={css.mapLayout}>
                <div className={css.map}>
                    {GROUPS.map((group) => (
                        <section key={group.title} className={css.group}>
                            <h4>{group.title}</h4>
                            <div className={css.tables}>
                                {group.tables.map((t) => (
                                    <button
                                        key={t.name}
                                        type="button"
                                        className={`${css.tableCard} ${t.name === selected ? css.selected : ''}`}
                                        aria-pressed={t.name === selected}
                                        onMouseEnter={() => setSelected(t.name)}
                                        onFocus={() => setSelected(t.name)}
                                        onClick={() => setSelected(t.name)}
                                    >
                                        <code>wyrd.{t.name}</code>
                                    </button>
                                ))}
                            </div>
                            <p>{group.note}</p>
                        </section>
                    ))}
                </div>
                <aside className={css.detail} aria-live="polite">
                    <code className={css.detailName}>wyrd.{table.name}</code>
                    <p>{table.holds}</p>
                    <dl>
                        <dt>How long</dt>
                        <dd>
                            {isMember(table.keeps) ? (
                                <>
                                    <code>Retention.{table.keeps}</code>,
                                    counted from {table.from}
                                </>
                            ) : (
                                table.keeps
                            )}
                        </dd>
                    </dl>
                </aside>
            </div>
            <figcaption className={css.caption}>
                Wyrd’s tables live in your own <code>DbContext</code> (
                <code>modelBuilder.AddWyrd()</code>), in the <code>wyrd</code>{' '}
                schema: they are in your migrations and commit in the same
                transaction as your own tables. Point at a table to see what it
                keeps.
            </figcaption>
        </figure>
    )
}
