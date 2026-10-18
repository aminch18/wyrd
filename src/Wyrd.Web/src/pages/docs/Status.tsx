import type { ReactNode } from 'react'
import styles from '../../styles/site.module.css'

export type StatusKind = 'implemented' | 'planned' | 'limit'

/** Planned items carry the version in which the design document plans them. */
export function Status({
    kind,
    version,
}: {
    kind: StatusKind
    version?: string
}) {
    const label =
        kind === 'implemented'
            ? 'Implemented'
            : kind === 'planned'
              ? `Planned${version ? ` in ${version}` : ''}`
              : 'Conscious limit'
    return (
        <span className={`${styles['status-badge']} ${styles[kind]}`}>
            {label}
        </span>
    )
}

export interface RuleRow {
    rule: ReactNode
    status: StatusKind
    version?: string
    /** What happens today, when the rule is planned. */
    today?: ReactNode
}

/** A table of rules, each with a visible implemented / planned marker. */
export function RuleTable({
    rows,
    first = 'Rule',
}: {
    rows: RuleRow[]
    first?: string
}) {
    return (
        <div className={styles['doc-table']}>
            <table>
                <thead>
                    <tr>
                        <th>{first}</th>
                        <th>Status</th>
                    </tr>
                </thead>
                <tbody>
                    {rows.map((row, index) => (
                        <tr key={index}>
                            <td className={styles['rule-cell']}>
                                {row.rule}
                                {row.today && (
                                    <>
                                        <br />
                                        <em>Today: </em>
                                        {row.today}
                                    </>
                                )}
                            </td>
                            <td className={styles['status-cell']}>
                                <Status
                                    kind={row.status}
                                    version={row.version}
                                />
                            </td>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    )
}
