import { Children, isValidElement, type ReactNode } from 'react'
import Markdown, { defaultUrlTransform } from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { CodeBlock } from '../../components/common/CodeBlock'
import { DocArticle } from './DocArticle'
import styles from '../../styles/site.module.css'

interface MarkdownArticleProps {
    source: string
    title: string
    lead: string
    eyebrow: string
}

function FencedCode({ children }: { children?: ReactNode }) {
    const child = Children.only(children)
    if (!isValidElement<{ children?: ReactNode; className?: string }>(child))
        return null
    const language = child.props.className?.replace('language-', '')
    const supported =
        language === 'csharp' ||
        language === 'shellscript' ||
        language === 'yaml'
    return (
        <CodeBlock
            code={String(child.props.children ?? '').replace(/\n$/, '')}
            language={supported ? language : 'text'}
        />
    )
}

const GUIDE_LINKS: Record<string, string> = {
    'from-ddd-to-wyrd.md': '#/docs/from-ddd',
    'philosophy.md': '#/docs/overview',
}

export function MarkdownArticle({
    source,
    title,
    lead,
    eyebrow,
}: MarkdownArticleProps) {
    return (
        <DocArticle title={title} lead={lead} eyebrow={eyebrow}>
            <Markdown
                remarkPlugins={[remarkGfm]}
                urlTransform={(url) =>
                    GUIDE_LINKS[url] ?? defaultUrlTransform(url)
                }
                components={{
                    pre: FencedCode,
                    table: ({ children }) => (
                        <div className={styles['doc-table']}>
                            <table>{children}</table>
                        </div>
                    ),
                }}
            >
                {source.replace(/^# .+\r?\n/, '')}
            </Markdown>
        </DocArticle>
    )
}
