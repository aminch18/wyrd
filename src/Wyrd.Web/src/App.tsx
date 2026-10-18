import styles from './styles/site.module.css'
import { lazy, Suspense } from 'react'
import { Footer } from './components/layout/Footer'
import { Header } from './components/layout/Header'
import { useHashRoute } from './hooks/useHashRoute'
import { useTheme } from './hooks/useTheme'
import { HomePage } from './pages/HomePage'

const DocsPage = lazy(() =>
    import('./pages/DocsPage').then((module) => ({ default: module.DocsPage })),
)

export function App() {
    const route = useHashRoute()
    const { theme, toggleTheme } = useTheme()

    return (
        <div className={styles['app']} data-theme={theme}>
            <Header
                page={route.page}
                theme={theme}
                onThemeChange={toggleTheme}
            />
            {route.page === 'home' ? (
                <HomePage />
            ) : (
                <Suspense
                    fallback={
                        <main className={styles['docs-page']} aria-busy="true">
                            <p className={styles['section-shell']}>
                                Loading documentation…
                            </p>
                        </main>
                    }
                >
                    <DocsPage section={route.section} />
                </Suspense>
            )}
            <Footer />
        </div>
    )
}
