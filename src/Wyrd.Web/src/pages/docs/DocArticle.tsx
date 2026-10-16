import styles from '../../styles/site.module.css'
import type { ReactNode } from 'react'
import { Github } from 'lucide-react'

interface DocArticleProps {
    eyebrow: string
    title: string
    lead: string
    children: ReactNode
}

export function DocArticle({
    eyebrow,
    title,
    lead,
    children,
}: DocArticleProps) {
    return (
        <article className={styles['doc-article']}>
            <p className={styles['doc-eyebrow']}>{eyebrow}</p>
            <h1>{title}</h1>
            <p className={styles['doc-lead']}>{lead}</p>
            <div className={styles['doc-divider']} />
            {children}
            <div className={styles['edit-link']}>
                <Github size={15} /> Documentation follows the repository source
                of truth.
            </div>
        </article>
    )
}
