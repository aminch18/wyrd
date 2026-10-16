import styles from '../../styles/site.module.css'
import { Callout } from '../../components/common/Callout'
import { CodeBlock } from '../../components/common/CodeBlock'
import { SNIPPETS } from '../../data/docs'
import { GUARANTEES } from '../../data/marketing'
import { DocArticle } from './DocArticle'

export function OverviewDoc() {
    return (
        <DocArticle
            eyebrow="Mental model"
            title="Overview & philosophy"
            lead="Wyrd treats determinism as the foundation, not an optional testing technique."
        >
            <h2>Pure core, framework-owned effects</h2>
            <p>
                User-facing deciders and steps are pure functions. They cannot
                publish, persist, schedule, or reach into infrastructure. Wyrd
                performs those effects around the business model.
            </p>
            <div className={styles['doc-principles']}>
                {GUARANTEES.map(({ title, copy, icon: Icon }) => (
                    <div key={title}>
                        <Icon size={20} />
                        <h3>{title}</h3>
                        <p>{copy}</p>
                    </div>
                ))}
            </div>
            <h2>One model, two syntaxes</h2>
            <p>
                The fluent and pipe APIs build the same immutable{' '}
                <code>Process</code>. Choose the expression style your team
                reads most naturally.
            </p>
            <CodeBlock
                code={SNIPPETS.pipeProcess}
                title="OrderFulfillment.cs"
            />
            <Callout title="The process is data">
                Because a process is data, the framework can render it, validate
                it, simulate its possible futures, and replay its recorded past.
            </Callout>
        </DocArticle>
    )
}
