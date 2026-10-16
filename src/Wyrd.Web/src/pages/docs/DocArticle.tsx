import type { ReactNode } from 'react'
import { Github } from 'lucide-react'

interface DocArticleProps {
  eyebrow: string
  title: string
  lead: string
  children: ReactNode
}

export function DocArticle({ eyebrow, title, lead, children }: DocArticleProps) {
  return (
    <article className="doc-article">
      <p className="doc-eyebrow">{eyebrow}</p>
      <h1>{title}</h1>
      <p className="doc-lead">{lead}</p>
      <div className="doc-divider" />
      {children}
      <div className="edit-link"><Github size={15} /> Documentation follows the repository source of truth.</div>
    </article>
  )
}
