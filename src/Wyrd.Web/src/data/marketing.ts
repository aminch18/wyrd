import type { LucideIcon } from 'lucide-react'
import { Clock3, Code2, Network, PackageCheck, Route, ShieldCheck, Sparkles } from 'lucide-react'

export interface Feature {
  title: string
  copy: string
  icon: LucideIcon
}

interface Norn {
  name: string
  tense: string
  label: string
  status: string
  copy: string
  icon: LucideIcon
}

export const GUARANTEES: Feature[] = [
  { title: 'Pure decisions', copy: 'Business rules stay as ordinary functions. No database, publisher, clock, or hidden I/O.', icon: Code2 },
  { title: 'Effects owned by Wyrd', copy: 'Retries, timeouts, idempotency, compensation, and delivery belong to the framework.', icon: ShieldCheck },
  { title: 'Processes are data', copy: 'The same model can be run, drawn, inspected, simulated, and eventually replayed.', icon: Route },
  { title: 'Contracts that resist drift', copy: 'Roslyn analyzers catch broken public events while the change is still in your editor.', icon: PackageCheck },
]

export const NORNS: Norn[] = [
  { name: 'Skuld', tense: 'what will be', label: 'Simulate', status: 'In progress', icon: Sparkles, copy: 'Explore failure, retry, duplicate, reordering, and timeout paths before production finds them.' },
  { name: 'Urd', tense: 'what was', label: 'Replay', status: 'Planned', icon: Clock3, copy: 'Turn the exact history of a production process into a deterministic local test.' },
  { name: 'Verdandi', tense: 'what is', label: 'Run', status: 'Core available', icon: Network, copy: 'Execute one message and commit state, events, inbox, outbox, and timers atomically.' },
]
