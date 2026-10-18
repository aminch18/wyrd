import { useEffect, useState } from 'react'

export type Theme = 'dark' | 'light'

const STORAGE_KEY = 'wyrd-theme'

function initialTheme(): Theme {
    const stored = window.localStorage.getItem(STORAGE_KEY)
    if (stored === 'dark' || stored === 'light') return stored
    return window.matchMedia('(prefers-color-scheme: light)').matches
        ? 'light'
        : 'dark'
}

export function useTheme() {
    const [theme, setTheme] = useState<Theme>(initialTheme)

    useEffect(() => {
        window.localStorage.setItem(STORAGE_KEY, theme)
        document.documentElement.style.colorScheme = theme
        document
            .querySelector('meta[name="theme-color"]')
            ?.setAttribute('content', theme === 'dark' ? '#0c1413' : '#eef4f2')
    }, [theme])

    return {
        theme,
        toggleTheme: () =>
            setTheme((current) => (current === 'dark' ? 'light' : 'dark')),
    }
}
