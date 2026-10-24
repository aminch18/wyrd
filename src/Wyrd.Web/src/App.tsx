import styles from './styles/site.module.css'
import type { ReactNode } from 'react'
import { Footer } from './components/layout/Footer'
import { Header } from './components/layout/Header'
import type { Route } from './app/navigation'
import { useTheme } from './hooks/useTheme'

export function App({
    route,
    children,
}: {
    route: Route
    children: ReactNode
}) {
    const { theme, toggleTheme } = useTheme()
    return (
        <div className={styles['app']} data-theme={theme}>
            <Header
                page={route.page}
                theme={theme}
                onThemeChange={toggleTheme}
            />
            {children}
            <Footer />
        </div>
    )
}
