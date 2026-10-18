import styles from '../../styles/site.module.css'
import { Callout } from '../../components/common/Callout'
import { CodeBlock } from '../../components/common/CodeBlock'
import { SNIPPETS } from '../../data/docs'
import { DocArticle } from './DocArticle'

const FINDINGS = [
    [
        'An undo without retries leaves stock reserved after a single transient failure.',
        'A step with an undo needs a retry policy, and .OnCompensated(...) can .Retry(...) too. Planned in v0.3: every step declares its policy, and undos always insist.',
    ],
    [
        'A redelivered failure reply consumed an extra retry.',
        'Replies carry their attempt: stale faults are ignored, a late success is always accepted.',
    ],
    [
        'A command the process gave up on still ran later, and its stock was never released.',
        'Its undo fences the original command: if it never ran, it never will; if it ran, it is undone.',
    ],
    [
        'A late event moved an order from Cancelled back to Released.',
        'Read models tolerate reordering: statuses only move forward.',
    ],
] as const

const LESSONS = [
    [
        'Retries are decided, never assumed, and undos insist.',
        'An undo that gives up after one transient failure leaves the world inconsistent. Today an undo uses its step\'s retry policy; planned in v0.3: every step declares .Retry(...) or .NoRetry("why"), and undos always retry with their own adjustable policy.',
    ],
    [
        'A step given up on is fenced, never guessed.',
        'When a process gives up on a step, a delayed copy may still execute. The undo carries the original command id and the owner decides atomically: undo it if it ran, fence it if it never did. A domain rejection is never proof that there was nothing to undo.',
    ],
    [
        'Read models tolerate reordering.',
        'Events from different slices arrive in any order: a late event must not move a view backwards.',
    ],
    [
        'Stale replies are expected.',
        'Redelivered replies and replies to earlier attempts are ignored; a success is welcome whenever it arrives.',
    ],
] as const

export function SimulationDoc() {
    return (
        <DocArticle
            eyebrow="Skuld · what will be"
            title="Simulation"
            lead="Run your scenario in hundreds of hostile futures before production does, and replay any broken one exactly."
        >
            <h2>The futures your tests never see</h2>
            <p>
                In production, commands fail transiently, brokers deliver
                messages twice and events arrive in any order. Skuld runs a
                scenario in many seeded futures with those perturbations,
                settles the clock so every retry and timeout fires, and checks
                your invariants at the end of each one. It runs entirely on the
                in-memory runtime: no broker, no database, milliseconds per
                future.
            </p>
            <CodeBlock code={SNIPPETS.skuld} title="ShopSimulation.cs" />
            <h2>Invariants</h2>
            <p>
                An invariant returns <code>null</code> when it holds, or a
                sentence describing what is wrong.{' '}
                <code>Invariant.Defaults</code> checks that no process is left
                half-way and that nothing ended needing a human; add the rules
                your business can never break, such as stock that is never lost.
            </p>
            <h2>Read the report, replay the future</h2>
            <p>
                Each broken invariant comes with the first future that broke it:
                the perturbations Skuld injected, the complete journal and the
                seed. <code>options.Only(seed)</code> replays exactly that
                future, as many times as you need to debug it.
            </p>
            <CodeBlock
                code={SNIPPETS.skuldReport}
                language="text"
                title="Test output"
            />
            <Callout title="A misconfigured simulation fails">
                Zero runs or rates outside <code>[0, 1]</code> throw instead of
                producing a green report that explored nothing.
            </Callout>
            <h2>What it found in our own sample</h2>
            <p>
                The first time we pointed Skuld at the sample shop, it broke 85
                of 200 futures. Every finding became a fix in Wyrd or in the
                sample:
            </p>
            <div className={styles['doc-table']}>
                <table>
                    <thead>
                        <tr>
                            <th>Skuld found</th>
                            <th>The fix</th>
                        </tr>
                    </thead>
                    <tbody>
                        {FINDINGS.map(([found, fix]) => (
                            <tr key={found}>
                                <td>{found}</td>
                                <td>{fix}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
            <p>
                After the fixes the shop holds in every one of 5,000 futures
                with 20% faults, 20% duplicates and 50% reordering: stock is
                never lost, and no order is left open silently.
            </p>
            <h2>Four rules for distributed processes</h2>
            <ol>
                {LESSONS.map(([rule, why]) => (
                    <li key={rule}>
                        <strong>{rule}</strong> {why}
                    </li>
                ))}
            </ol>
        </DocArticle>
    )
}
