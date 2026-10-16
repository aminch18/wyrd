import { useEffect } from 'react'
import { Github } from 'lucide-react'
import { GITHUB_URL } from '../app/links'
import { DOC_COMPONENTS } from './docs/articles'
import { DocsNav } from './docs/DocsNav'

export function DocsPage({ section }: { section: string }) {
  const current = DOC_COMPONENTS[section] ? section : 'quick-start'
  const Content = DOC_COMPONENTS[current]

  useEffect(() => {
    window.scrollTo({ top: 0 })
  }, [current])

  return (
    <main className="docs-page">
      <div className="docs-shell">
        <DocsNav current={current} />
        <Content />
        <aside className="docs-outline">
          <p>On this page</p>
          <span>{current.replace('-', ' ')}</span>
          <a href={GITHUB_URL} target="_blank" rel="noreferrer"><Github size={14} /> View source</a>
        </aside>
      </div>
    </main>
  )
}
