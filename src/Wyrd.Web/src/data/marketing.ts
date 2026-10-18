import type { LucideIcon } from 'lucide-react'
import {
    Clock3,
    Code2,
    Network,
    PackageCheck,
    Route,
    ShieldCheck,
    Sparkles,
} from 'lucide-react'

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
    {
        title: 'Pure decisions',
        copy: 'Business rules stay as ordinary functions. No database, publisher, clock, or hidden I/O.',
        icon: Code2,
    },
    {
        title: 'Effects owned by Wyrd',
        copy: 'Retries, timeouts, idempotency, compensation, and delivery belong to the framework.',
        icon: ShieldCheck,
    },
    {
        title: 'Processes are data',
        copy: 'Run, draw, inspect, and simulate one model today, with replay on the roadmap.',
        icon: Route,
    },
    {
        title: 'Contracts that resist drift',
        copy: 'Roslyn analyzers catch broken public events while the change is still in your editor.',
        icon: PackageCheck,
    },
]

export const NORNS: Norn[] = [
    {
        name: 'Skuld',
        tense: 'what will be',
        label: 'Simulate',
        status: 'Available',
        icon: Sparkles,
        copy: 'Run your scenario in hundreds of hostile futures, with faults, duplicates, and reordering, and replay any broken one by its seed.',
    },
    {
        name: 'Urd',
        tense: 'what was',
        label: 'Replay',
        status: 'Available',
        icon: Clock3,
        copy: 'Capture an in-memory process instance and replay its recorded replies and clock as a deterministic local test.',
    },
    {
        name: 'Verdandi',
        tense: 'what is',
        label: 'Run',
        status: 'Core available',
        icon: Network,
        copy: 'Execute one message and commit state, events, inbox, outbox, and timers atomically.',
    },
]
