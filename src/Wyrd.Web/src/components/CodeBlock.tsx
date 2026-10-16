import React, { useState } from 'react'
import { Check, Copy } from 'lucide-react'

interface CodeBlockProps {
  code: string
  language?: string
  title?: string
}

const KEYWORDS = new Set([
  'public', 'sealed', 'record', 'class', 'abstract', 'static', 'override',
  'using', 'namespace', 'var', 'new', 'await', 'when', 'switch', 'return',
  'partial', 'enum', 'object', 'decimal', 'string', 'int', 'void'
])

function highlightCSharpLine(line: string): React.ReactNode[] {
  const commentIndex = line.indexOf('//')
  const codePart = commentIndex >= 0 ? line.slice(0, commentIndex) : line
  const commentPart = commentIndex >= 0 ? line.slice(commentIndex) : ''

  const tokenRegex = /(".*?"|\b[A-Z][A-Za-z0-9_]*\b|\b[a-z_][A-Za-z0-9_]*\b|\b\d+(?:_\d+)*(?:\.\d+)?m?\b|\[[A-Za-z0-9_]+(?:\([^\]]*\))?\])/g
  const parts: React.ReactNode[] = []
  let lastIdx = 0
  let match: RegExpExecArray | null

  while ((match = tokenRegex.exec(codePart)) !== null) {
    if (match.index > lastIdx) {
      parts.push(codePart.slice(lastIdx, match.index))
    }
    const token = match[0]
    if (token.startsWith('"')) {
      parts.push(<span key={match.index} className="tok-str">{token}</span>)
    } else if (token.startsWith('[')) {
      parts.push(<span key={match.index} className="tok-attr">{token}</span>)
    } else if (KEYWORDS.has(token)) {
      parts.push(<span key={match.index} className="tok-kw">{token}</span>)
    } else if (/^[A-Z]/.test(token)) {
      parts.push(<span key={match.index} className="tok-type">{token}</span>)
    } else if (/^\d/.test(token)) {
      parts.push(<span key={match.index} className="tok-num">{token}</span>)
    } else {
      parts.push(token)
    }
    lastIdx = match.index + token.length
  }

  if (lastIdx < codePart.length) {
    parts.push(codePart.slice(lastIdx))
  }

  if (commentPart) {
    parts.push(<span key="comment" className="tok-comment">{commentPart}</span>)
  }

  return parts
}

export function CodeBlock({ code, language = 'csharp', title }: CodeBlockProps) {
  const [copied, setCopied] = useState(false)

  const handleCopy = async () => {
    await navigator.clipboard.writeText(code)
    setCopied(true)
    setTimeout(() => setCopied(false), 1800)
  }

  const lines = code.trim().split('\n')

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
          <button
            type="button"
            onClick={handleCopy}
            className="copy-btn"
            aria-label="Copy code"
            title="Copy to clipboard"
          >
            {copied ? <Check size={14} /> : <Copy size={14} />}
            <span>{copied ? 'Copied' : 'Copy'}</span>
          </button>
        </div>
      </div>
      <pre className="code-pre">
        <code>
          {lines.map((line, i) => (
            <div key={i} className="code-line">
              {language === 'csharp' ? highlightCSharpLine(line) : line}
            </div>
          ))}
        </code>
      </pre>
    </div>
  )
}
