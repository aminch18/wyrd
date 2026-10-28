import { useEffect, useState } from 'react'
import { Lock, RotateCcw, Zap } from 'lucide-react'
import { Controls } from './Controls'
import { useStepper } from './useStepper'
import css from './verdandi.module.css'

/** The transaction of guides/how-verdandi-works.md, "One message, one transaction", line by line. */
const STEPS = [
    {
        code: '',
        say: 'A worker looks for work. ReserveStock for one SKU is the next due row of the queue.',
    },
    { code: 'BEGIN', say: 'One transaction for the whole message.' },
    {
        code: 'take the next due row of queue',
        hint: 'FOR UPDATE SKIP LOCKED (PostgreSQL) / UPDLOCK, READPAST (SQL Server)',
        say: 'The row is locked by this worker: every other worker passes over it.',
    },
    {
        code: 'check inbox / handled',
        hint: 'a duplicate is answered, or ignored',
        say: 'Seen this message id? No: it is handled for the first time.',
    },
    {
        code: 'read the state',
        hint: 'the SKU, at version 7',
        say: 'The SKU’s state is read, with its version.',
    },
    {
        code: 'decide',
        hint: 'your pure code',
        say: 'The engine calls your decider: one unit reserved, StockReserved. Nothing is written yet.',
    },
    {
        code: 'write events, queue rows, views, the inbox row',
        hint: 'UPDATE ... WHERE version = 7',
        say: 'Everything the message changed is written, inside the transaction: nobody else sees it yet. (A message that moves a process writes its instance here too.)',
    },
    {
        code: 'delete the queue row',
        say: 'Taking the message off the queue is part of the same transaction.',
    },
    {
        code: 'COMMIT',
        say: 'All of it happens at once: the state, the event, the reply for the process, the view and the outcome.',
    },
]

const LAST = STEPS.length - 1
const WRITE = 6
const DELETE = 7
/** How long the rollback stays on screen before another worker takes the row. */
const RETRY_AFTER = 2600

type Phase = 'none' | 'pending' | 'committed'

export function MessageLifecycle() {
    const stepper = useStepper(LAST, 1500)
    const [attempt, setAttempt] = useState(1)
    const [crashedAt, setCrashedAt] = useState<number | null>(null)
    const step = stepper.step
    const crashed = crashedAt !== null
    const worker = `worker ${attempt}`

    const retry = () => {
        setCrashedAt(null)
        setAttempt(2)
        stepper.setStep(1)
        stepper.play()
    }

    // After a crash, the row is back in the queue: another worker takes it on its own.
    useEffect(() => {
        if (!crashed) return
        const timer = window.setTimeout(retry, RETRY_AFTER)
        return () => window.clearTimeout(timer)
    }, [crashed])

    const writes: Phase = crashed
        ? 'none'
        : step >= LAST
          ? 'committed'
          : step >= WRITE
            ? 'pending'
            : 'none'
    const rowLocked = !crashed && step >= 2 && step < LAST
    const rowDeleting = !crashed && step >= DELETE && step < LAST
    const rowGone = !crashed && step >= LAST
    const crashable = attempt === 1 && !crashed && step >= 1 && step < LAST

    const crash = () => {
        stepper.pause()
        setCrashedAt(step)
    }
    const reset = () => {
        setCrashedAt(null)
        setAttempt(1)
        stepper.reset()
    }
    const replay = () => {
        setAttempt(1)
        stepper.play()
    }
    const read = (from: number) =>
        step >= from && !crashed
            ? writes === 'none'
                ? 'read'
                : writes
            : 'none'

    return (
        <figure className={`${css.figure} ${css.big}`}>
            <ol className={css.attempts} aria-label="Attempts">
                <li
                    className={
                        attempt === 1 && !crashed
                            ? css.attemptNow
                            : attempt === 2 || crashed
                              ? css.attemptFailed
                              : ''
                    }
                >
                    <strong>attempt 1</strong> · worker 1
                    {(crashed || attempt === 2) && (
                        <span> · crashed, rolled back</span>
                    )}
                </li>
                {(crashed || attempt === 2) && (
                    <li
                        className={
                            attempt === 2
                                ? step === LAST
                                    ? css.attemptOk
                                    : css.attemptNow
                                : ''
                        }
                    >
                        <strong>attempt 2</strong> · worker 2
                        {attempt === 2 && step === LAST && (
                            <span> · committed</span>
                        )}
                        {crashed && <span> · takes the row in a moment</span>}
                    </li>
                )}
            </ol>

            <div className={css.lifecycle}>
                <section className={css.column} aria-label="The queue table">
                    <h4>queue</h4>
                    <div
                        className={[
                            css.row,
                            rowLocked && css.locked,
                            rowDeleting && css.deleting,
                            rowGone && css.gone,
                            crashed && css.returned,
                        ]
                            .filter(Boolean)
                            .join(' ')}
                    >
                        <span>ReserveStock · KEYBOARD</span>
                        {rowLocked && (
                            <span className={css.holder}>
                                <Lock size={12} aria-hidden /> {worker}
                            </span>
                        )}
                        {crashed && (
                            <span className={css.holder}>
                                <RotateCcw size={12} aria-hidden /> back,
                                unlocked
                            </span>
                        )}
                    </div>
                    <div className={css.row}>
                        <span>OrderPlaced · A-2</span>
                    </div>
                    <div className={`${css.row} ${css.later}`}>
                        <span>timer · A-1, due later</span>
                    </div>
                    <div className={`${css.row} ${css[`phase-${writes}`]}`}>
                        <span>reply · to the process</span>
                    </div>
                </section>

                <section
                    className={`${css.column} ${css.transaction} ${crashed ? css.rolledBack : ''}`}
                    aria-label="The worker's transaction"
                >
                    <h4>{worker}</h4>
                    <ol className={css.code}>
                        {STEPS.slice(1).map((s, index) => {
                            const at = index + 1
                            const state =
                                crashed && at > crashedAt
                                    ? css.skipped
                                    : at === step
                                      ? css.current
                                      : at < step
                                        ? css.done
                                        : ''
                            return (
                                <li key={s.code} className={state}>
                                    <code>{s.code}</code>
                                    {s.hint && <small>{s.hint}</small>}
                                </li>
                            )
                        })}
                    </ol>
                    {crashed && (
                        <div className={css.crash} role="status">
                            <Zap size={15} /> Worker 1 died before{' '}
                            <code>COMMIT</code>: the database rolls all of it
                            back.
                        </div>
                    )}
                </section>

                <section
                    className={css.column}
                    aria-label="What the message writes"
                >
                    <h4>tables</h4>
                    <Chip
                        name="inbox"
                        phase={read(3)}
                        label={
                            writes === 'none' ? 'not seen yet' : 'the outcome'
                        }
                    />
                    <Chip
                        name="states"
                        phase={
                            step >= 4 && !crashed
                                ? writes === 'none'
                                    ? 'read'
                                    : writes
                                : 'idle'
                        }
                        label={
                            writes === 'none' ? 'KEYBOARD v7' : 'KEYBOARD v8'
                        }
                    />
                    <Chip name="events" phase={writes} label="StockReserved" />
                    <Chip
                        name="views"
                        phase={writes}
                        label="stock of KEYBOARD"
                    />
                </section>
            </div>

            <figcaption className={css.caption} aria-live="polite">
                {crashed ? (
                    <>
                        <strong>Rolled back.</strong> No state without its
                        event, no event without its state: nothing is left half
                        done. The row is back in the queue, unlocked, and the
                        next worker takes it. A crash is not a decision of your
                        code, so there is nothing to compensate: the message is
                        simply handled again.
                    </>
                ) : attempt === 2 && step === LAST ? (
                    <>
                        <strong>Handled once.</strong> Worker 1’s attempt left
                        nothing behind, so worker 2 started from the same state
                        (version 7) and committed everything together.
                    </>
                ) : (
                    <>
                        <strong>
                            {step === 0 ? 'Ready.' : `Step ${step} of ${LAST}.`}
                        </strong>{' '}
                        {STEPS[step].say}
                    </>
                )}
            </figcaption>

            <Controls
                playing={stepper.playing}
                atStart={step === 0 && !crashed && attempt === 1}
                atEnd={step === LAST || crashed}
                playLabel={crashed ? 'Retry now' : undefined}
                onPlay={crashed ? retry : step === LAST ? replay : stepper.play}
                onPause={stepper.pause}
                onBack={stepper.back}
                onNext={stepper.next}
                onReset={reset}
            >
                <button
                    type="button"
                    className={css.danger}
                    onClick={crash}
                    disabled={!crashable}
                >
                    <Zap size={15} /> Crash before the commit
                </button>
            </Controls>
        </figure>
    )
}

function Chip({
    name,
    label,
    phase,
}: {
    name: string
    label: string
    phase: Phase | 'read' | 'idle'
}) {
    return (
        <div className={`${css.chip} ${css[`phase-${phase}`]}`}>
            <code>{name}</code>
            <span>{phase === 'none' ? '—' : label}</span>
        </div>
    )
}
