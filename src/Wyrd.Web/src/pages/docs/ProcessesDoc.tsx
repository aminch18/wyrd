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
                    rejection. Exhausted step retries suspend the instance with
                    a reason instead; <code>.OrCompensate()</code> opts into
                    compensating.
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
            <h2>Retries, undos and suspension</h2>
            <p>
                Every step states its own retry policy:{' '}
                <code>.Retry(attempts, backoff)</code> or{' '}
                <code>.NoRetry(&quot;why&quot;)</code>. There is no default, and
                analyzer WYRD0014 reports a step that declares neither.{' '}
                <Status kind="implemented" />
            </p>
            <p>
                An undo does not inherit its step&apos;s retries. Undos and the
                outcome command always insist: 10 retries with exponential
                backoff (waits capped at one minute) by default, adjustable with{' '}
                <code>.Undo(command, 20.Times)</code> and impossible to disable.{' '}
                <Status kind="implemented" />
            </p>
            <p>
                When a step&apos;s retries are exhausted, or an undo or the
                outcome gives up, the instance becomes <code>Suspended</code>{' '}
                with a reason (<code>ReasonOf</code>). An operator calls{' '}
                <code>ResumeAsync</code> or <code>CompensateAsync</code>, and a
                late success resumes it on its own.{' '}
                <code>.Retry(3.Times).OrCompensate()</code> opts a step into
                compensating instead. <Status kind="implemented" /> The rules
                and the reasons are in{' '}
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
