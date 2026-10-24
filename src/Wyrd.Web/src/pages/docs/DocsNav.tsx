import styles from '../../styles/site.module.css'
import { ChevronRight } from 'lucide-react'
import { navigate, routePath } from '../../app/navigation'
import { DOC_SECTIONS } from '../../data/docs'

export function DocsNav({ current }: { current: string }) {
    return (
        <aside className={styles['docs-sidebar']}>
            <p>Documentation</p>
            <label className={styles['docs-mobile-nav']}>
                Chapter
                <select
                    value={current}
                    onChange={(event) => navigate('docs', event.target.value)}
                >
                    {(['Why', 'Behavior', 'Guides', 'Reference'] as const).map(
                        (group) => (
                            <optgroup label={group} key={group}>
                                {DOC_SECTIONS.filter(
                                    (item) => item.group === group,
                                ).map((item) => (
                                    <option key={item.id} value={item.id}>
                                        {item.title}
                                    </option>
                                ))}
                            </optgroup>
                        ),
                    )}
                </select>
            </label>
            <nav aria-label="Documentation sections">
                {(['Why', 'Behavior', 'Guides', 'Reference'] as const).map(
                    (group) => (
                        <div className={styles['docs-nav-group']} key={group}>
                            <h2>{group}</h2>
                            {DOC_SECTIONS.filter(
                                (item) => item.group === group,
                            ).map((item) => (
                                <a
                                    className={
                                        current === item.id
                                            ? styles.current
                                            : ''
                                    }
                                    key={item.id}
                                    aria-current={
                                        current === item.id ? 'page' : undefined
                                    }
                                    href={routePath('docs', item.id)}
                                >
                                    <span>{item.title}</span>
                                    <ChevronRight size={14} />
                                </a>
                            ))}
                        </div>
                    ),
                )}
            </nav>
            <div className={styles['docs-side-note']}>
                <span>Public preview</span>
                <p>
                    The APIs are tested, but the project is still evolving
                    quickly.
                </p>
            </div>
        </aside>
    )
}
