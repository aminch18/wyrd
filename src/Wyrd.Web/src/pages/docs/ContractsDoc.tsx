import { CodeBlock } from '../../components/common/CodeBlock'
import { SNIPPETS } from '../../data/docs'
import { ANALYZER_RULES } from '../../data/docs'
import styles from '../../styles/site.module.css'
import { DocArticle } from './DocArticle'

export function ContractsDoc() {
    return (
        <DocArticle
            eyebrow="Boundaries"
            title="Versioned contracts"
            lead="Refactor internal facts freely while keeping external promises explicit and reviewable."
        >
            <h2>Map internal facts deliberately</h2>
            <CodeBlock
                code={SNIPPETS.publication}
                title="PaymentChargedPublication.cs"
            />
            <h2>Review the contract as text</h2>
            <p>
                Every integration event is captured in{' '}
                <code>contracts.wyrd.txt</code>. Breaking changes fail the
                build; compatible additions receive an IDE code fix.
            </p>
            <CodeBlock
                code={SNIPPETS.contractError}
                language="text"
                title="Contract diagnostic"
            />
            <h2>Analyzer rules</h2>
            <div className={styles['rule-table']}>
                {ANALYZER_RULES.map((rule) => (
                    <div key={rule.code}>
                        <code>{rule.code}</code>
                        <span
                            className={[
                                styles.severity,
                                styles[rule.severity.toLowerCase()],
                            ].join(' ')}
                        >
                            {rule.severity}
                        </span>
                        <p>{rule.description}</p>
                    </div>
                ))}
            </div>
        </DocArticle>
    )
}
