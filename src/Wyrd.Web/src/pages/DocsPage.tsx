import styles from '../styles/site.module.css'
import { useEffect } from 'react'
import { Github } from 'lucide-react'
import { GITHUB_URL } from '../app/links'
import { DOC_COMPONENTS } from './docs/articles'
import { DOC_SECTIONS } from '../data/docs'
import { DocsNav } from './docs/DocsNav'

export function DocsPage({ section }: { section: string }) {
    const current = DOC_COMPONENTS[section] ? section : 'overview'
    const Content = DOC_COMPONENTS[current]

    useEffect(() => {
        window.scrollTo({ top: 0 })
    }, [current])

    return (
        <main className={styles['docs-page']}>
            <div className={styles['docs-shell']}>
                <DocsNav current={current} />
                <Content />
                <aside className={styles['docs-outline']}>
                    <p>On this page</p>
                    <span>
                        {
                            DOC_SECTIONS.find((item) => item.id === current)
                                ?.title
                        }
                    </span>
                    <a href={GITHUB_URL} target="_blank" rel="noreferrer">
                        <Github size={14} /> View source
                    </a>
                </aside>
            </div>
        </main>
    )
}
