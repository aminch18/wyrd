import { useEffect, useState } from 'react'
import { Check, Copy, FileCode2, Terminal } from 'lucide-react'
import styles from './CodeBlock.module.css'

interface CodeBlockProps {
    code: string
    language?: 'csharp' | 'shellscript' | 'yaml' | 'text'
    title?: string
}

export function CodeBlock({
    code,
    language = 'csharp',
    title,
}: CodeBlockProps) {
    const [copied, setCopied] = useState(false)
    const [highlighted, setHighlighted] = useState<{
        code: string
        language: string
        html: string
    }>()
    const source = code.trim()

    useEffect(() => {
        let active = true
        import('./highlighter')
            .then(({ highlight }) => highlight(source, language))
            .then((html) => {
                if (active) setHighlighted({ code: source, language, html })
            })
            .catch(() => {
                /* The readable plain-text fallback remains available. */
            })
        return () => {
            active = false
        }
    }, [source, language])

    const handleCopy = async () => {
        await navigator.clipboard.writeText(source)
        setCopied(true)
        window.setTimeout(() => setCopied(false), 1800)
    }

    const html =
        highlighted?.code === source && highlighted.language === language
            ? highlighted.html
            : undefined
    const Icon = language === 'shellscript' ? Terminal : FileCode2

    return (
        <div className={styles.card} data-code-block>
            <div className={styles.header}>
                <div className={styles.caption}>
                    <Icon size={14} />
                    <span>{title ?? language}</span>
                </div>
                <div className={styles.actions}>
                    <span className={styles.language}>
                        {language === 'shellscript'
                            ? 'shell'
                            : language === 'csharp'
                              ? 'C#'
                              : language}
                    </span>
                    <button
                        type="button"
                        onClick={handleCopy}
                        className={styles.copy}
                        aria-label="Copy code"
                    >
                        {copied ? <Check size={14} /> : <Copy size={14} />}
                        <span aria-live="polite">
                            {copied ? 'Copied' : 'Copy'}
                        </span>
                    </button>
                </div>
            </div>
            {html ? (
                <div
                    className={styles.syntax}
                    dangerouslySetInnerHTML={{ __html: html }}
                />
            ) : (
                <div className={styles.syntax}>
                    <pre>
                        <code>{source}</code>
                    </pre>
                </div>
            )}
        </div>
    )
}
