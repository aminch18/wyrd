import { useEffect, useState } from 'react'

export type Theme = 'dark' | 'light'

const STORAGE_KEY = 'wyrd-theme'

function initialTheme(): Theme {
    let stored: string | null = null
    try {
        stored = window.localStorage.getItem(STORAGE_KEY)
    } catch {
        /* Storage may be disabled. */
    }
    if (stored === 'dark' || stored === 'light') return stored
    return window.matchMedia('(prefers-color-scheme: light)').matches
        ? 'light'
        : 'dark'
}

export function useTheme() {
    const [theme, setTheme] = useState<Theme>('dark')
    const [ready, setReady] = useState(false)

    useEffect(() => {
        setTheme(initialTheme())
        setReady(true)
    }, [])

    useEffect(() => {
        if (!ready) return
        try {
            window.localStorage.setItem(STORAGE_KEY, theme)
        } catch {
            /* Theme still works without persistence. */
        }
        document.documentElement.style.colorScheme = theme
        document
            .querySelector('meta[name="theme-color"]')
            ?.setAttribute('content', theme === 'dark' ? '#0c1413' : '#eef4f2')
    }, [theme, ready])

    return {
        theme,
        toggleTheme: () =>
            setTheme((current) => (current === 'dark' ? 'light' : 'dark')),
    }
}
