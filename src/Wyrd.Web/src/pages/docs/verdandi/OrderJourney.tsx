import { useEffect, type ReactNode } from 'react'
import { Clock, Globe, Send, Workflow } from 'lucide-react'
import { CodeBlock } from '../../../components/common/CodeBlock'
import { LiveArchitecture, type Path } from './ArchitectureCanvas'
import { Controls } from './Controls'
import { useStepper } from './useStepper'
import css from './verdandi.module.css'

interface Beat {
    who: 'api' | 'worker' | 'outside'
    title: ReactNode
    detail: ReactNode
    writes: string[]
    /** The queue after this transaction commits. */
    queue: { label: string; timer?: boolean }[]
    process: { status: string; note?: string } | null
    /** What this transaction lights up on the architecture. */
    path: Path
}

/** guides/how-verdandi-works.md, "One order, end to end", one transaction per beat. */
const BEATS: Beat[] = [
    {
        who: 'api',
        title: (
            <>
                <code>POST /orders</code> → <code>SendAsync(PlaceOrder)</code>
            </>
        ),
        detail: (
            <>
                Seen this message id? No. The order’s state is read, your pure
                code decides, the state is written (version checked),{' '}
                <code>OrderPlaced</code> is recorded and queued, and the outcome
                goes to the inbox.{' '}
                <strong>202 Accepted, with the decision.</strong>
            </>
        ),
        writes: ['inbox', 'states', 'events', 'queue'],
        queue: [{ label: 'OrderPlaced' }],
        process: null,
        path: {
            edges: ['http', 'send', 'decide', 'tx'],
            nodes: [
                'clients',
                'endpoints',
                'runtime',
                'engine',
                'inbox',
                'states',
                'events',
                'queue',
            ],
        },
    },
    {
        who: 'worker',
        title: (
            <>
                worker takes <code>OrderPlaced</code>
            </>
        ),
        detail: (
            <>
                Starts the fulfillment process, queues ReserveStock, updates the
                views.
            </>
        ),
        writes: ['handled', 'instances', 'queue', 'views'],
        queue: [{ label: 'ReserveStock' }],
        process: { status: 'Running', note: 'started: reserving stock' },
        path: {
            edges: ['take', 'workerDecide'],
            nodes: [
                'workers',
                'engine',
                'queue',
                'inbox',
                'instances',
                'views',
            ],
        },
    },
    {
        who: 'worker',
        title: (
            <>
                worker takes <code>ReserveStock</code>
            </>
        ),
        detail: (
            <>
                The SKU’s state decides: StockReserved, and a reply to the
                process.
            </>
        ),
        writes: ['inbox', 'states', 'events', 'queue'],
        queue: [{ label: 'the reply' }],
        process: { status: 'Running', note: 'reserving stock' },
        path: {
            edges: ['take', 'workerDecide'],
            nodes: ['workers', 'engine', 'queue', 'inbox', 'states', 'events'],
        },
    },
    {
        who: 'worker',
        title: <>worker takes the reply</>,
        detail: <>The process moves on: ChargePayment is queued.</>,
        writes: ['instances', 'queue'],
        queue: [{ label: 'ChargePayment' }],
        process: { status: 'Running', note: 'charging the payment' },
        path: {
            edges: ['take', 'workerDecide'],
            nodes: ['workers', 'engine', 'queue', 'instances'],
        },
    },
    {
        who: 'worker',
        title: (
            <>
                worker takes <code>ChargePayment</code>, then its reply
            </>
        ),
        detail: (
            <>
                The process waits for PaymentConfirmed: a timer row, due in 15
                minutes.
            </>
        ),
        writes: ['instances', 'queue'],
        queue: [{ label: 'timer · due in 15 minutes', timer: true }],
        process: {
            status: 'Awaiting',
            note: 'PaymentConfirmed, 15 minutes at most',
        },
        path: {
            edges: ['take', 'workerDecide'],
            nodes: ['workers', 'engine', 'queue', 'instances'],
        },
    },
    {
        who: 'outside',
        title: (
            <>
                payment confirmed → <code>PublishAsync(PaymentConfirmed)</code>
            </>
        ),
        detail: <>The fact from outside is queued like any message.</>,
        writes: ['queue'],
        queue: [
            { label: 'timer · due in 15 minutes', timer: true },
            { label: 'PaymentConfirmed' },
        ],
        process: {
            status: 'Awaiting',
            note: 'PaymentConfirmed, 15 minutes at most',
        },
        path: {
            edges: ['receive', 'tx'],
            nodes: ['services', 'runtime', 'queue'],
        },
    },
    {
        who: 'worker',
        title: (
            <>
                worker takes <code>PaymentConfirmed</code>
            </>
        ),
        detail: (
            <>
                The wait ends: its timer row is deleted in the same transaction,
                and ShipOrder is queued.
            </>
        ),
        writes: ['handled', 'instances', 'queue'],
        queue: [{ label: 'ShipOrder' }],
        process: { status: 'Running', note: 'shipping' },
        path: {
            edges: ['take', 'workerDecide'],
            nodes: ['workers', 'engine', 'queue', 'inbox', 'instances'],
        },
    },
    {
        who: 'worker',
        title: <>… until the process completes</>,
        detail: (
            <>
                Each message the same way: one transaction, taken off the queue
                as it commits.
            </>
        ),
        writes: [],
        queue: [],
        process: { status: 'Completed' },
        path: { edges: ['take'], nodes: ['workers', 'queue', 'instances'] },
    },
]

const WHO = {
    api: { label: 'your API', Icon: Send },
    worker: { label: 'a worker', Icon: Workflow },
    outside: { label: 'from outside', Icon: Globe },
} as const

const LAST = BEATS.length

export function OrderJourney({ source }: { source: string }) {
    const stepper = useStepper(LAST, 1700)
    const step = stepper.step
    const { setStep } = stepper
    // A step can be linked to: ?order=5.
    useEffect(() => {
        const linked = Number(
            new URLSearchParams(window.location.search).get('order'),
        )
        if (linked >= 1 && linked <= LAST) setStep(linked)
    }, [setStep])
    const beat = step > 0 ? BEATS[step - 1] : undefined
    const queue = beat?.queue ?? []
    const process = beat?.process ?? null

    return (
        <figure className={`${css.figure} ${css.big}`}>
            <LiveArchitecture
                path={beat?.path}
                label="The architecture, lit by the order's current transaction"
            />
            <div className={css.journey}>
                <ol
                    className={css.timeline}
                    aria-label="Transactions, in order"
                >
                    {BEATS.map((b, index) => {
                        const at = index + 1
                        const { label, Icon } = WHO[b.who]
                        return (
                            <li
                                key={index}
                                className={[
                                    css.tx,
                                    css[`who-${b.who}`],
                                    at === step
                                        ? css.txNow
                                        : at < step
                                          ? css.txDone
                                          : css.txLater,
                                ].join(' ')}
                            >
                                <button
                                    type="button"
                                    onClick={() => stepper.setStep(at)}
                                >
                                    <span className={css.txDot}>
                                        <Icon size={14} aria-hidden />
                                    </span>
                                    <span className={css.txBody}>
                                        <span className={css.txWho}>
                                            {label}
                                            {b.writes.length > 0 && (
                                                <> · one transaction</>
                                            )}
                                        </span>
                                        <span className={css.txTitle}>
                                            {b.title}
                                        </span>
                                        {at === step && (
                                            <>
                                                <span className={css.txDetail}>
                                                    {b.detail}
                                                </span>
                                                {b.writes.length > 0 && (
                                                    <span
                                                        className={css.txWrites}
                                                    >
                                                        {b.writes.map((w) => (
                                                            <code key={w}>
                                                                {w}
                                                            </code>
                                                        ))}
                                                    </span>
                                                )}
                                            </>
                                        )}
                                    </span>
                                </button>
                            </li>
                        )
                    })}
                </ol>

                <aside className={css.journeyState} aria-live="polite">
                    <section className={css.statePanel}>
                        <h4>the client</h4>
                        <p className={step >= 1 ? css.stateOk : ''}>
                            {step === 0
                                ? 'sends POST /orders'
                                : '202 Accepted, with the decision'}
                        </p>
                        {step >= 2 && (
                            <small>
                                Already answered: it never waits for the rest of
                                the process.
                            </small>
                        )}
                    </section>
                    <section className={css.statePanel}>
                        <h4>queue</h4>
                        {queue.length === 0 ? (
                            <p className={css.empty}>
                                {step === 0 ? 'nothing yet' : 'empty'}
                            </p>
                        ) : (
                            <ul className={css.queueRows}>
                                {queue.map((row) => (
                                    <li
                                        key={`${step}-${row.label}`}
                                        className={`${css.row} ${row.timer ? css.timerRow : ''}`}
                                    >
                                        {row.timer && (
                                            <Clock size={13} aria-hidden />
                                        )}
                                        <span>{row.label}</span>
                                    </li>
                                ))}
                            </ul>
                        )}
                    </section>
                    <section className={css.statePanel}>
                        <h4>the fulfillment process</h4>
                        {process ? (
                            <p className={css[`status-${process.status}`]}>
                                <strong>{process.status}</strong>
                                {process.note && <> · {process.note}</>}
                            </p>
                        ) : (
                            <p className={css.empty}>not started</p>
                        )}
                    </section>
                </aside>
            </div>

            <Controls
                playing={stepper.playing}
                atStart={step === 0}
                atEnd={step === LAST}
                onPlay={stepper.play}
                onPause={stepper.pause}
                onBack={stepper.back}
                onNext={stepper.next}
                onReset={stepper.reset}
            />
            <details className={css.textVersion}>
                <summary>The same, as text</summary>
                <CodeBlock
                    code={source}
                    language="text"
                    title="the Shop’s order"
                />
            </details>
        </figure>
    )
}
