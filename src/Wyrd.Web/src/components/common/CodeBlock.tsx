import type { ReactNode } from 'react'
import { useState } from 'react'
import { Check, Copy } from 'lucide-react'

interface CodeBlockProps {
  code: string
  language?: string
  title?: string
}

const KEYWORDS = new Set([
  'abstract', 'await', 'class', 'decimal', 'enum', 'int', 'namespace', 'new',
  'object', 'override', 'partial', 'public', 'record', 'return', 'sealed',
  'static', 'string', 'using', 'var', 'void', 'when',
])

const CONTROL_KEYWORDS = new Set(['else', 'for', 'foreach', 'if', 'switch', 'while'])
const TOKEN = /\/\/.*$|"(?:\\.|[^"\\])*"|\[[A-Za-z0-9_]+(?:\([^\]]*\))?\]|\b\d+(?:_\d+)*(?:\.\d+)?m?\b|\b[A-Za-z_][A-Za-z0-9_]*\b/g

function tokenClass(token: string, source: string, start: number, end: number): string | undefined {
  if (token.startsWith('//')) return 'tok-comment'
  if (token.startsWith('"')) return 'tok-str'
  if (token.startsWith('[')) return 'tok-attr'
  if (/^\d/.test(token)) return 'tok-num'
  if (CONTROL_KEYWORDS.has(token)) return 'tok-control'
  if (KEYWORDS.has(token)) return 'tok-kw'
  const next = source.slice(end).trimStart()
  const prefix = source.slice(0, start).trimEnd()
  if ((next.startsWith('(') || next.startsWith('<')) && !/\b(?:class|new|record)\s*$/.test(prefix)) return 'tok-fn'
  if (/^[A-Z]/.test(token)) return 'tok-type'
  return 'tok-variable'
}

function highlightCSharpLine(line: string): ReactNode[] {
  const parts: ReactNode[] = []
  let cursor = 0

  for (const match of line.matchAll(TOKEN)) {
    const index = match.index
    if (index > cursor) parts.push(line.slice(cursor, index))

    const token = match[0]
    const className = tokenClass(token, line, index, index + token.length)
    parts.push(className ? <span key={index} className={className}>{token}</span> : token)
    cursor = index + token.length
  }

  if (cursor < line.length) parts.push(line.slice(cursor))
  return parts
}

export function CodeBlock({ code, language = 'csharp', title }: CodeBlockProps) {
  const [copied, setCopied] = useState(false)

  const handleCopy = async () => {
    await navigator.clipboard.writeText(code)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1800)
  }

  return (
    <div className="code-card">
      <div className="code-header">
        <div className="code-header-left">
          <span className="code-dot dot-red" />
          <span className="code-dot dot-amber" />
          <span className="code-dot dot-green" />
          {title && <span className="code-title">{title}</span>}
        </div>
        <div className="code-header-right">
          <span className="code-lang">{language}</span>
          <button type="button" onClick={handleCopy} className="copy-btn" aria-label="Copy code" title="Copy to clipboard">
            {copied ? <Check size={14} /> : <Copy size={14} />}
            <span aria-live="polite">{copied ? 'Copied' : 'Copy'}</span>
          </button>
        </div>
      </div>
      <pre className="code-pre"><code>{code.trim().split('\n').map((line, index) => (
          <span key={index} className="code-line">
            {language === 'csharp' ? highlightCSharpLine(line) : line}
          </span>
        ))}</code></pre>
    </div>
  )
}
