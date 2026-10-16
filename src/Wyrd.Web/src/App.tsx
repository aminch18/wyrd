import styles from './styles/site.module.css'
import { Footer } from './components/layout/Footer'
import { Header } from './components/layout/Header'
import { useHashRoute } from './hooks/useHashRoute'
import { useTheme } from './hooks/useTheme'
import { DocsPage } from './pages/DocsPage'
import { HomePage } from './pages/HomePage'

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
                <DocsPage section={route.section} />
            )}
            <Footer />
        </div>
    )
}
