import { useState } from 'react'
import { Lock, Zap } from 'lucide-react'
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

type Phase = 'none' | 'pending' | 'committed'

export function MessageLifecycle() {
    const stepper = useStepper(LAST, 1700)
    const [crashedAt, setCrashedAt] = useState<number | null>(null)
    const step = stepper.step
    const crashed = crashedAt !== null

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
    const crashable = !crashed && step >= 1 && step < LAST

    const crash = () => {
        stepper.pause()
        setCrashedAt(step)
    }
    const reset = () => {
        setCrashedAt(null)
        stepper.reset()
    }

    return (
        <figure className={css.figure}>
            <div className={css.lifecycle}>
                <section className={css.column} aria-label="The queue table">
                    <h4>queue</h4>
                    <div
                        className={[
                            css.row,
                            rowLocked && css.locked,
                            rowDeleting && css.deleting,
                            rowGone && css.gone,
                        ]
                            .filter(Boolean)
                            .join(' ')}
                    >
                        <span>ReserveStock · KEYBOARD</span>
                        {rowLocked && <Lock size={13} aria-label="locked" />}
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
                    <h4>worker</h4>
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
                            <Zap size={15} /> The process died before{' '}
                            <code>COMMIT</code>: the database rolls everything
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
                        phase={
                            step >= 3 && !crashed
                                ? writes === 'none'
                                    ? 'read'
                                    : writes
                                : 'none'
                        }
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
                        <strong>Rolled back.</strong> The row is back in the
                        queue, unlocked, and another worker handles it. No state
                        saved without its event, no event without its state:
                        nothing is left half done.
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
                atStart={step === 0 && !crashed}
                atEnd={step === LAST || crashed}
                onPlay={crashed ? reset : stepper.play}
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
