import { useState } from 'react'
import api from '../../data/reference/api.json'
import analyzers from '../../data/reference/analyzers.json'
import { GITHUB_URL } from '../../app/links'
import { CodeBlock } from '../../components/common/CodeBlock'
import { DocArticle } from './DocArticle'
import styles from '../../styles/site.module.css'

type Entry = (typeof api)[number]
const GROUPS = [
    'Messages',
    'Decisions',
    'Processes',
    'Runtime',
    'Projections',
    'Testing',
    'Replay (Urd)',
    'Analyzers',
]

function ApiEntry({ entry, search }: { entry: Entry; search: string }) {
    const members = entry.members.filter(
        (member) =>
            !search ||
            entry.name.toLowerCase().includes(search) ||
            member.signature.toLowerCase().includes(search),
    )
    const [open, setOpen] = useState(false)
    return (
        <details
            className={styles['api-entry']}
            onToggle={(event) => setOpen(event.currentTarget.open)}
        >
            <summary>
                <code>{entry.name}</code>
                <span>{entry.description}</span>
            </summary>
            {open && (
                <>
                    <p>
                        <a href={`${GITHUB_URL}/blob/main/${entry.source}`}>
                            View source · {entry.source}
                        </a>
                    </p>
                    <CodeBlock
                        code={
                            entry.members.length
                                ? entry.signatures[0]
                                : entry.signatures.join('\n\n')
                        }
                        title="Declaration excerpts · C#"
                    />
                    {members.map((member, index) => (
                        <div key={index} className={styles['api-member']}>
                            <pre className={styles['api-example']}>
                                <code>{member.signature}</code>
                            </pre>
                            <p>{member.description}</p>
                            <pre className={styles['api-example']}>
                                <code>{member.example}</code>
                            </pre>
                        </div>
                    ))}
                    <CodeBlock
                        code={entry.example}
                        title="Example · application context"
                    />
                </>
            )}
        </details>
    )
}

export function ApiReferenceDoc() {
    const [group, setGroup] = useState('Messages')
    const [query, setQuery] = useState('')
    const search = query.trim().toLowerCase()
    const matches = api.filter(
        (entry) =>
            (search || entry.group === group) &&
            `${entry.name} ${entry.description} ${entry.signatures.join(' ')}`
                .toLowerCase()
                .includes(search),
    )
    const rules = analyzers.filter((rule) =>
        `${rule.code} ${rule.title}`.toLowerCase().includes(search),
    )
    const toolMatch = /cs8509|wyrdspr0001|suppress|fix all|codefix/.test(search)
    return (
        <DocArticle
            eyebrow="Reference · checked against source"
            title="API reference"
            lead="Find a signature after you understand the model. Every public type in Wyrd and Wyrd.Testing is indexed here, with source links and a short example."
        >
            <p>
                The build checks these declaration excerpts against the
                repository, verifies coverage of public types, every fluent and
                pipe builder member, every runtime member and all analyzer
                rules. Examples use application types from the Shop or show a
                small model to adapt; they are excerpts, rather than complete
                programs. Generic constraints and implementation details remain
                in the linked source.
            </p>
            <p>
                Start with <a href="#/docs/overview">the philosophy</a>,{' '}
                <a href="#/docs/semantics">behavior</a> and the{' '}
                <a href="#/docs/read-models">read-model guide</a>. The current
                host is in memory; durable transports and integrations remain
                planned.
            </p>
            <div className={styles['api-controls']}>
                <label>
                    Topic
                    <select
                        value={group}
                        onChange={(event) => setGroup(event.target.value)}
                    >
                        {GROUPS.map((name) => (
                            <option key={name}>{name}</option>
                        ))}
                    </select>
                </label>
                <label>
                    Search all signatures and rules
                    <input
                        type="search"
                        value={query}
                        onChange={(event) => setQuery(event.target.value)}
                        placeholder="ResumeAsync, Retry, WYRD0300…"
                    />
                </label>
            </div>
            <p role="status">
                {search ? `Results for “${query}”` : group}. Open an entry for
                its declarations and example.
            </p>
            {(search || group !== 'Analyzers') &&
                matches.map((entry) => (
                    <ApiEntry key={entry.name} entry={entry} search={search} />
                ))}
            {((search && (rules.length > 0 || toolMatch)) ||
                (!search && group === 'Analyzers')) && (
                <>
                    <p>
                        Rules below are all currently declared diagnostics, not
                        a continuous range of numbers. WYRD0010 was removed;
                        WYRD0014 requires an explicit retry decision.
                    </p>
                    {rules.map((rule) => (
                        <details
                            className={styles['api-entry']}
                            key={rule.code}
                        >
                            <summary>
                                <code>
                                    {rule.code} · {rule.severity}
                                </code>
                                <span>{rule.title}.</span>
                            </summary>
                            <p>{rule.fix}</p>
                            <p>
                                <a
                                    href={`${GITHUB_URL}/blob/main/${rule.source}`}
                                >
                                    Diagnostic declaration · {rule.code}
                                </a>
                            </p>
                            <pre className={styles['api-example']}>
                                <code>{rule.example}</code>
                            </pre>
                        </details>
                    ))}
                    <h2>Exhaustive union switch suppressor</h2>
                    <p>
                        <code>WYRDSPR0001 → CS8509</code> suppresses the
                        compiler's missing-case warning only when a switch over
                        a Union handles every declared case; WYRD0020 still
                        reports missing cases.
                    </p>
                    <p>
                        <a
                            href={`${GITHUB_URL}/blob/main/src/Wyrd.Analyzers/UnionSwitchSuppressor.cs`}
                        >
                            Suppressor source
                        </a>
                    </p>
                    <pre className={styles['api-example']}>
                        <code>{`payment switch\n{\n    Payment.None => "none",\n    Payment.Charged => "charged",\n    Payment.Refunded => "refunded",\n};`}</code>
                    </pre>
                    <h2>IDE code fixes and Fix all</h2>
                    <p>
                        <a
                            href={`${GITHUB_URL}/blob/main/src/Wyrd.Analyzers.CodeFixes/ProcessStyleCodeFix.cs`}
                        >
                            ProcessStyleCodeFix
                        </a>{' '}
                        converts WYRD0013 processes and supports batch Fix all.
                    </p>
                    <p>
                        <a
                            href={`${GITHUB_URL}/blob/main/src/Wyrd.Analyzers.CodeFixes/ContractSnapshotCodeFix.cs`}
                        >
                            ContractSnapshotCodeFix
                        </a>{' '}
                        handles WYRD0200 and WYRD0202, preserving snapshot
                        comments and coordinating all entries.
                    </p>
                    <p>
                        <a
                            href={`${GITHUB_URL}/blob/main/src/Wyrd.Analyzers.CodeFixes/JsonContextCodeFix.cs`}
                        >
                            JsonContextCodeFix
                        </a>{' '}
                        handles WYRD0300, WYRD0301 and WYRD0302, including
                        collisions revealed by newly declared cases.
                    </p>
                    <p>
                        For each provider, choose the lightbulb action, then Fix
                        all in document, project or solution. Contract and JSON
                        fixes use dedicated coordinated providers; syntax
                        conversion uses Roslyn's batch provider.
                    </p>
                </>
            )}
            {search &&
                matches.length === 0 &&
                rules.length === 0 &&
                !toolMatch && (
                    <p>No matching entry. Try a type or diagnostic number.</p>
                )}
        </DocArticle>
    )
}
