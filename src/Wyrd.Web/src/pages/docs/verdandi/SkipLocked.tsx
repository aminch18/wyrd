import { Lock } from 'lucide-react'
import { Controls } from './Controls'
import { useStepper } from './useStepper'
import css from './verdandi.module.css'

const ROWS = [
    { label: 'OrderPlaced · A-1', ticks: 2 },
    { label: 'ReserveStock · KEYBOARD', ticks: 3 },
    { label: 'ChargePayment · A-2', ticks: 1 },
    { label: 'reply · A-1', ticks: 2 },
    { label: 'OrderPlaced · A-3', ticks: 2 },
    { label: 'ShipOrder · A-4', ticks: 3 },
    { label: 'reply · A-2', ticks: 1 },
    { label: 'PaymentConfirmed · A-5', ticks: 2 },
]
const WORKERS = ['worker 1', 'worker 2', 'worker 3']

interface Tick {
    /** Who holds each row now: a worker's index, or null. */
    owner: (number | null)[]
    done: boolean[]
    /** Rows a worker passed over this tick because another worker held them. */
    skipped: { row: number; by: number }[]
    took: { row: number; by: number }[]
}

/** Every tick from the start, computed: stepping back shows exactly what was there. */
function simulate(): Tick[] {
    const owner: (number | null)[] = ROWS.map(() => null)
    const left = ROWS.map((r) => r.ticks)
    const done = ROWS.map(() => false)
    const ticks: Tick[] = [
        { owner: [...owner], done: [...done], skipped: [], took: [] },
    ]
    while (done.some((d) => !d)) {
        // Work, then commit: a row whose work is finished leaves the queue.
        owner.forEach((w, row) => {
            if (w === null) return
            left[row] -= 1
            if (left[row] === 0) {
                done[row] = true
                owner[row] = null
            }
        })
        const skipped: Tick['skipped'] = []
        const took: Tick['took'] = []
        WORKERS.forEach((_, worker) => {
            if (owner.includes(worker)) return
            for (let row = 0; row < ROWS.length; row++) {
                if (done[row]) continue
                if (owner[row] !== null) {
                    skipped.push({ row, by: worker })
                    continue
                }
                owner[row] = worker
                took.push({ row, by: worker })
                break
            }
        })
        ticks.push({ owner: [...owner], done: [...done], skipped, took })
    }
    return ticks
}

const TICKS = simulate()
const LAST = TICKS.length - 1

export function SkipLocked() {
    const stepper = useStepper(LAST, 1100)
    const tick = TICKS[stepper.step]
    const left = tick.done.filter((d) => !d).length

    return (
        <figure className={css.figure}>
            <div className={css.skipLayout}>
                <ul className={css.workers} aria-label="Workers">
                    {WORKERS.map((name, worker) => {
                        const row = tick.owner.indexOf(worker)
                        return (
                            <li key={name} className={row >= 0 ? css.busy : ''}>
                                <strong>{name}</strong>
                                <span>
                                    {row >= 0
                                        ? ROWS[row].label
                                        : left === 0
                                          ? 'nothing due: waits'
                                          : 'looking'}
                                </span>
                            </li>
                        )
                    })}
                </ul>
                <ol className={css.queueTable} aria-label="The queue table">
                    {ROWS.map((row, index) => {
                        const holder = tick.owner[index]
                        const passed = tick.skipped.filter(
                            (s) => s.row === index,
                        )
                        const taken = tick.took.some((t) => t.row === index)
                        return (
                            <li
                                key={row.label}
                                className={[
                                    css.row,
                                    holder !== null && css.locked,
                                    tick.done[index] && css.gone,
                                    taken && css.taken,
                                ]
                                    .filter(Boolean)
                                    .join(' ')}
                            >
                                <span>{row.label}</span>
                                {holder !== null && (
                                    <span className={css.holder}>
                                        <Lock size={12} aria-hidden />{' '}
                                        {WORKERS[holder]}
                                    </span>
                                )}
                                {passed.length > 0 && (
                                    <span className={css.skippedBy}>
                                        skipped by{' '}
                                        {passed
                                            .map((p) => WORKERS[p.by])
                                            .join(', ')}
                                    </span>
                                )}
                            </li>
                        )
                    })}
                </ol>
            </div>
            <figcaption className={css.caption} aria-live="polite">
                {stepper.step === 0 ? (
                    <>
                        <strong>
                            Eight messages, three workers, one table.
                        </strong>{' '}
                        Each worker takes the next due row that nobody holds.
                    </>
                ) : left === 0 ? (
                    <>
                        <strong>Queue drained.</strong> No worker ever waited
                        for another’s lock, and no message was handled twice.
                    </>
                ) : (
                    <>
                        <strong>
                            {left} {left === 1 ? 'message' : 'messages'} left.
                        </strong>{' '}
                        {tick.skipped.length > 0
                            ? 'A row someone else holds is skipped, not waited for.'
                            : 'A finished message leaves the queue in its own commit.'}
                    </>
                )}
            </figcaption>
            <Controls
                playing={stepper.playing}
                atStart={stepper.step === 0}
                atEnd={stepper.step === LAST}
                onPlay={stepper.play}
                onPause={stepper.pause}
                onBack={stepper.back}
                onNext={stepper.next}
                onReset={stepper.reset}
            />
        </figure>
    )
}
