import { CodeBlock } from '../../components/common/CodeBlock'
import { SNIPPETS } from '../../data/docs'
import { DocArticle } from './DocArticle'

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
                    Completed steps compensate in reverse order after rejection
                    or exhausted faults.
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
            <h2>Observe the exact history</h2>
            <CodeBlock
                code={SNIPPETS.journalOutput}
                language="text"
                title="Journal"
            />
        </DocArticle>
    )
}
