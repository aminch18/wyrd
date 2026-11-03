/**
 * The concurrency benchmark's numbers, copied from benchmarks/README.md
 * ("Concurrency on PostgreSQL", 2026-10-27). Nothing here is derived or rounded:
 * a change to the README's tables changes this file by hand.
 */

export interface Point {
    x: number
    y: number
    /** Shown in the tooltip and the table, e.g. a second run. */
    note?: string
}

export interface Series {
    id: 'spread' | 'hot'
    label: string
    before: Point[]
    after: Point[]
}

/** Commands/s from N concurrent SendAsync callers, 2,000 calls per run. */
export const COMMANDS: Series[] = [
    {
        id: 'spread',
        label: 'a different order each',
        before: [
            { x: 1, y: 495 },
            { x: 8, y: 2624 },
            { x: 32, y: 3624 },
            { x: 128, y: 3262 },
        ],
        after: [
            { x: 1, y: 714 },
            { x: 32, y: 2782, note: 'first of two runs' },
            { x: 32, y: 3483, note: 'second of two runs' },
        ],
    },
    {
        id: 'hot',
        label: 'the same SKU',
        before: [
            { x: 1, y: 652 },
            { x: 8, y: 587 },
            { x: 32, y: 497 },
            { x: 128, y: 397 },
        ],
        after: [],
    },
]

/** Calls on the same SKU that still conflicted after 5 attempts, of 2,000. */
export const HOT_FAILED = {
    before: [
        { callers: 8, failed: 1014 },
        { callers: 32, failed: 1271 },
        { callers: 128, failed: 1335 },
    ],
    after: [{ callers: 8, failed: 994 }],
}

/** Messages/s with N workers draining 1,000 placed and paid orders. */
export const WORKERS: Series[] = [
    {
        id: 'spread',
        label: 'one SKU per order',
        before: [
            { x: 1, y: 491 },
            { x: 2, y: 875 },
            { x: 4, y: 1252 },
            { x: 8, y: 1676 },
        ],
        after: [
            { x: 1, y: 591 },
            { x: 8, y: 2340 },
        ],
    },
    {
        id: 'hot',
        label: 'the same SKU',
        before: [
            { x: 1, y: 466 },
            { x: 2, y: 794 },
            { x: 4, y: 951 },
            { x: 8, y: 1001 },
        ],
        after: [{ x: 8, y: 1319 }],
    },
]

export const MACHINE =
    'PostgreSQL 17 in Docker, .NET 10, Apple M3 Pro, 2026-10-27, one process'
