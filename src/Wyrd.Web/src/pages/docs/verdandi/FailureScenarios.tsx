import { useState, type KeyboardEvent, type ReactNode } from 'react'
import { LiveArchitecture, type Path } from './ArchitectureCanvas'
import { Controls } from './Controls'
import { useStepper } from './useStepper'
import css from './verdandi.module.css'

type Tone = 'ok' | 'retry' | 'stop'

interface Beat {
    who: string
    text: ReactNode
    tone?: Tone
}

interface Scenario {
    id: string
    tab: string
    beats: Beat[]
    lesson: ReactNode
}

/** guides/how-verdandi-works.md, "When things fail" and "Duplicates". */
const SCENARIOS: Scenario[] = [
    {
        id: 'version',
        tab: 'Two writers, one row',
        beats: [
            {
                who: 'worker A',
                text: (
                    <>
                        reads <code>KEYBOARD</code> at version 7
                    </>
                ),
            },
            {
                who: 'worker B',
                text: (
                    <>
                        reads <code>KEYBOARD</code> at version 7
                    </>
                ),
            },
            {
                who: 'worker A',
                text: (
                    <>
                        <code>UPDATE … WHERE version = 7</code>: one row, now
                        version 8. Commits.
                    </>
                ),
                tone: 'ok',
            },
            {
                who: 'worker B',
                text: (
                    <>
                        <code>UPDATE … WHERE version = 7</code>: no row matches.
                        Rolls back.
                    </>
                ),
                tone: 'retry',
            },
            {
                who: 'worker B',
                text: (
                    <>
                        handles the message again, against what A wrote (version
                        8)
                    </>
                ),
                tone: 'ok',
            },
        ],
        lesson: (
            <>
                Not a failure: nothing is counted, and nothing is lost. States
                and instances are both written this way.
            </>
        ),
    },
    {
        id: 'send',
        tab: 'SendAsync runs out of attempts',
        beats: [
            {
                who: 'your API',
                text: (
                    <>
                        <code>SendAsync(Restock)</code> while many callers write
                        the same SKU
                    </>
                ),
            },
            {
                who: 'attempt 1',
                text: <>conflicts: tried again at once</>,
                tone: 'retry',
            },
            {
                who: 'attempts 2–4',
                text: <>conflict too: each tried again at once</>,
                tone: 'retry',
            },
            {
                who: 'attempt 5',
                text: (
                    <>
                        conflicts: that was <code>MaxConflictRetries</code>{' '}
                        attempts in all (5 by default)
                    </>
                ),
                tone: 'stop',
            },
            {
                who: 'your API',
                text: (
                    <>
                        gets a <code>ConcurrencyConflictException</code>,
                        whatever the database reported (a stale version, a
                        unique key, a deadlock). Wyrd wrote nothing.
                    </>
                ),
                tone: 'stop',
            },
        ],
        lesson: (
            <>
                Retry later, or model the hot spot differently (split the stock
                of a SKU into buckets, or let a process serialize the requests).
                An integration command’s outside call may have been made
                already: sending the command again is a new message, with a new
                idempotency key.
            </>
        ),
    },
    {
        id: 'step',
        tab: 'A process step fails',
        beats: [
            {
                who: 'process',
                text: (
                    <>
                        <code>.Then(Payment.Charge).Retry(3.Times)</code>: the
                        step’s command fails
                    </>
                ),
                tone: 'retry',
            },
            {
                who: 'process',
                text: (
                    <>
                        applies the step’s own retry policy: the command is sent
                        again
                    </>
                ),
                tone: 'retry',
            },
            { who: 'process', text: <>the retries run out</>, tone: 'stop' },
            {
                who: 'instance',
                text: (
                    <>
                        is <strong>suspended</strong> for an operator; it never
                        expires while it waits
                    </>
                ),
                tone: 'stop',
            },
            {
                who: 'operator',
                text: <>resumes it, or compensates it</>,
                tone: 'ok',
            },
        ],
        lesson: (
            <>
                One retry layer: the host never retries a process command behind
                the process’s back. A step your code <em>rejects</em> (a
                business decision) is compensated at once, and a step can opt
                into compensating on its own when its retries run out (
                <code>.Retry(3.Times).OrCompensate()</code>).
            </>
        ),
    },
    {
        id: 'handler',
        tab: 'A handler throws',
        beats: [
            {
                who: 'worker',
                text: (
                    <>
                        a projection throws while handling an event: the
                        transaction rolls back
                    </>
                ),
                tone: 'retry',
            },
            {
                who: 'queue',
                text: <>the message is tried again later</>,
                tone: 'retry',
            },
            {
                who: 'queue',
                text: (
                    <>
                        each time with a longer delay; the messages behind it
                        keep flowing
                    </>
                ),
                tone: 'retry',
            },
            {
                who: 'queue',
                text: (
                    <>
                        after <code>MaxDeliveryAttempts</code> (10 by default)
                        it moves to <code>dead_letters</code>
                    </>
                ),
                tone: 'stop',
            },
            {
                who: 'you',
                text: (
                    <>
                        look at it; it is kept for{' '}
                        <code>Retention.DeadLetters</code>
                    </>
                ),
            },
        ],
        lesson: (
            <>
                Anything that isn’t a process step fails this way: an event
                handler, a reply.
            </>
        ),
    },
    {
        id: 'integration',
        tab: 'An integration, under a lease',
        beats: [
            {
                who: 'worker',
                text: (
                    <>
                        takes a lease on the queued integration command; no
                        transaction is open
                    </>
                ),
            },
            {
                who: 'worker',
                text: (
                    <>
                        calls the other system with an idempotency key, the same
                        on every attempt
                    </>
                ),
            },
            {
                who: 'worker',
                text: (
                    <>
                        if it stops mid-call, the lease runs out (
                        <code>IntegrationLease</code>) and another worker calls
                        again, with the same key
                    </>
                ),
                tone: 'retry',
            },
            {
                who: 'worker',
                text: <>records the outcome in a second transaction</>,
                tone: 'ok',
            },
            {
                who: 'other system',
                text: (
                    <>
                        if it honours the key, a second call never repeats the
                        charge
                    </>
                ),
                tone: 'ok',
            },
        ],
        lesson: (
            <>
                Pass the idempotency key to the other system: Wyrd makes the
                call at least once, the key makes its effect happen once.
            </>
        ),
    },
]

/** What each beat lights up on the architecture, and what is in trouble then. */
const LIVE: Record<string, { path: Path; alert?: string[] }[]> = {
    version: [
        { path: { edges: ['take'], nodes: ['workers', 'states'] } },
        { path: { edges: ['take'], nodes: ['workers', 'states'] } },
        { path: { edges: ['take'], nodes: ['workers', 'states'] } },
        {
            path: { edges: ['take'], nodes: ['workers', 'states'] },
            alert: ['workers'],
        },
        {
            path: {
                edges: ['take', 'workerDecide'],
                nodes: ['workers', 'engine', 'states'],
            },
        },
    ],
    send: [
        {
            path: {
                edges: ['send', 'decide', 'tx'],
                nodes: ['endpoints', 'runtime', 'engine', 'states'],
            },
        },
        {
            path: { edges: ['tx'], nodes: ['runtime', 'states'] },
            alert: ['states'],
        },
        {
            path: { edges: ['tx'], nodes: ['runtime', 'states'] },
            alert: ['states'],
        },
        {
            path: { edges: ['tx'], nodes: ['runtime', 'states'] },
            alert: ['runtime'],
        },
        {
            path: { edges: ['send'], nodes: ['endpoints', 'runtime'] },
            alert: ['endpoints'],
        },
    ],
    step: [
        {
            path: { edges: ['take'], nodes: ['workers', 'queue', 'instances'] },
            alert: ['workers'],
        },
        {
            path: {
                edges: ['take', 'workerDecide'],
                nodes: ['workers', 'engine', 'queue', 'instances'],
            },
        },
        {
            path: { edges: ['take'], nodes: ['workers', 'instances'] },
            alert: ['workers'],
        },
        { path: { edges: [], nodes: ['instances'] }, alert: ['instances'] },
        { path: { edges: ['take'], nodes: ['workers', 'queue', 'instances'] } },
    ],
    handler: [
        {
            path: { edges: ['take'], nodes: ['workers', 'queue'] },
            alert: ['workers'],
        },
        { path: { edges: ['take'], nodes: ['workers', 'queue'] } },
        { path: { edges: ['take'], nodes: ['workers', 'queue'] } },
        {
            path: { edges: ['take'], nodes: ['workers', 'dead'] },
            alert: ['dead'],
        },
        { path: { edges: [], nodes: ['dead'] } },
    ],
    integration: [
        { path: { edges: ['take'], nodes: ['workers', 'queue'] } },
        { path: { edges: ['call'], nodes: ['workers', 'systems'] } },
        {
            path: { edges: ['call'], nodes: ['workers', 'systems'] },
            alert: ['workers'],
        },
        {
            path: {
                edges: ['take'],
                nodes: ['workers', 'states', 'events', 'queue', 'inbox'],
            },
        },
        { path: { edges: ['call'], nodes: ['systems'] } },
    ],
}

export function FailureScenarios() {
    const [active, setActive] = useState(0)
    const scenario = SCENARIOS[active]
    const last = scenario.beats.length
    const stepper = useStepper(last, 1300)
    const live =
        stepper.step > 0 ? LIVE[scenario.id]?.[stepper.step - 1] : undefined

    const select = (index: number) => {
        setActive(index)
        stepper.reset()
    }
    const onKey = (event: KeyboardEvent) => {
        const move =
            event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0
        if (move === 0) return
        event.preventDefault()
        const next = (active + move + SCENARIOS.length) % SCENARIOS.length
        select(next)
        document.getElementById(`failure-tab-${SCENARIOS[next].id}`)?.focus()
    }

    return (
        <figure className={`${css.figure} ${css.big}`}>
            <LiveArchitecture
                path={live?.path}
                alert={live?.alert}
                label="The architecture, lit by the failure's current moment"
            />
            <div
                role="tablist"
                aria-label="Failure scenarios"
                className={css.tabs}
                onKeyDown={onKey}
            >
                {SCENARIOS.map((s, index) => (
                    <button
                        key={s.id}
                        id={`failure-tab-${s.id}`}
                        type="button"
                        role="tab"
                        aria-selected={index === active}
                        aria-controls="failure-panel"
                        tabIndex={index === active ? 0 : -1}
                        onClick={() => select(index)}
                    >
                        {s.tab}
                    </button>
                ))}
            </div>
            <div
                id="failure-panel"
                role="tabpanel"
                aria-labelledby={`failure-tab-${scenario.id}`}
                className={css.panel}
            >
                <ol className={css.beats}>
                    {scenario.beats.map((beat, index) => (
                        <li
                            key={index}
                            className={[
                                index < stepper.step ? css.shown : css.hidden,
                                beat.tone && css[beat.tone],
                            ]
                                .filter(Boolean)
                                .join(' ')}
                        >
                            <span className={css.who}>{beat.who}</span>
                            <span>{beat.text}</span>
                        </li>
                    ))}
                </ol>
                <p
                    className={`${css.lesson} ${stepper.step >= last ? css.shown : css.hidden}`}
                >
                    {scenario.lesson}
                </p>
            </div>
            <Controls
                playing={stepper.playing}
                atStart={stepper.step === 0}
                atEnd={stepper.step === last}
                onPlay={stepper.play}
                onPause={stepper.pause}
                onBack={stepper.back}
                onNext={stepper.next}
                onReset={stepper.reset}
            />
        </figure>
    )
}
