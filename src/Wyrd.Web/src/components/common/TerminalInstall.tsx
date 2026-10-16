import { useState } from 'react'
import { Check, Copy } from 'lucide-react'

const INSTALL_COMMAND = 'dotnet add package Wyrd --prerelease'

export function TerminalInstall() {
  const [copied, setCopied] = useState(false)

  const copy = async () => {
    await navigator.clipboard.writeText(INSTALL_COMMAND)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1600)
  }

  return (
    <div className="install-command">
      <span aria-hidden="true">$</span>
      <code>{INSTALL_COMMAND}</code>
      <button type="button" onClick={copy} aria-label="Copy install command">
        {copied ? <Check size={16} /> : <Copy size={16} />}
      </button>
    </div>
  )
}
