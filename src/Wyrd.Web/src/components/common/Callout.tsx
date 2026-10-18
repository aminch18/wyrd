import styles from '../../styles/site.module.css'
import type { ReactNode } from 'react'
import { Sparkles } from 'lucide-react'

export function Callout({
    title,
    children,
}: {
    title: string
    children: ReactNode
}) {
    return (
        <aside className={styles['callout']}>
            <Sparkles size={18} />
            <div>
                <strong>{title}</strong>
                <p>{children}</p>
            </div>
        </aside>
    )
}
