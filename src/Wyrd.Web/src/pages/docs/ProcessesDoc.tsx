import { CodeBlock } from '../../components/common/CodeBlock'
import { SNIPPETS } from '../../data/docs'
import { DocArticle } from './DocArticle'
import { Status } from './Status'

export function ProcessesDoc() {
    return (
        <DocArticle
            eyebrow="Orchestration"
            title="Declarative processes"
            lead="Model long-running business flows without giving domain code an infrastructure API."
        >
            <h2>Read it like the business describes it</h2>
            <CodeBlock
                code={SNIPPETS.fluentProcess}
                title="OrderFulfillment.cs"
            />
            <h2>What the runtime guarantees</h2>
            <ul>
                <li>Each envelope is handled as one atomic change set.</li>
                <li>
                    Retries retain stable message identities and follow declared
                    backoff.
                </li>
                <li>
                    Completed steps compensate in reverse order after a
                    rejection. Today exhausted forward retries compensate too;
                    in v0.3 they suspend the instance instead.
                </li>
                <li>
                    A step given up on with an unknown outcome is fenced by its
                    undo: a late copy of it never executes.
                </li>
                <li>
                    Early awaited events are parked until the process reaches
                    the matching await.
                </li>
                <li>
                    Timeouts use the runtime clock; tests advance it without
                    sleeping.
                </li>
            </ul>
            <h2>Retries and undos</h2>
            <p>
                Every step in the example above states its own retry policy.
                That is the convention to follow, because a step&apos;s retry is
                a business decision. <Status kind="planned" version="v0.3" />{' '}
                The compiler will ask for it: each step will declare{' '}
                <code>.Retry(...)</code> or{' '}
                <code>.NoRetry(&quot;why&quot;)</code>. Today a step without{' '}
                <code>.Retry(...)</code> simply has no retries.
            </p>
            <p>
                An undo does not inherit its step&apos;s retries. Undos always
                insist, with a bounded, generous default that you can adjust
                with <code>.Undo(command, 10.Times)</code> but never disable.{' '}
                <Status kind="planned" version="v0.3" /> Until then, an undo
                uses the policy of its step. The rules and the reasons are in{' '}
                <a href="#/docs/semantics">How Wyrd behaves</a> and{' '}
                <a href="#/docs/decisions">Design decisions</a>.
            </p>
            <h2>Observe the exact history</h2>
            <CodeBlock
                code={SNIPPETS.journalOutput}
                language="text"
                title="Journal"
            />
        </DocArticle>
    )
}
