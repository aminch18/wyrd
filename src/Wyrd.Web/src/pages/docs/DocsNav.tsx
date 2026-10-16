import styles from '../../styles/site.module.css'
import { ChevronRight } from 'lucide-react'
import { navigate } from '../../app/navigation'
import { DOC_SECTIONS } from '../../data/docs'

const ITEMS = [
    {
        id: 'quick-start',
        title: 'Quick start',
        subtitle: 'Install and run the sample.',
    },
    ...DOC_SECTIONS,
]

export function DocsNav({ current }: { current: string }) {
    return (
        <aside className={styles['docs-sidebar']}>
            <p>Documentation</p>
            <nav aria-label="Documentation sections">
                {ITEMS.map((item) => (
                    <button
                        className={current === item.id ? styles.current : ''}
                        type="button"
                        key={item.id}
                        onClick={() => navigate('docs', item.id)}
                    >
                        <span>{item.title}</span>
                        <ChevronRight size={14} />
                    </button>
                ))}
            </nav>
            <div className={styles['docs-side-note']}>
                <span>Early alpha</span>
                <p>
                    The APIs are tested, but the project is still evolving
                    quickly.
                </p>
            </div>
        </aside>
    )
}
