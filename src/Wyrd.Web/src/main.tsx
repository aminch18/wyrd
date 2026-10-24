import { StrictMode } from 'react'
import { hydrateRoot, createRoot } from 'react-dom/client'
import { App } from './App'
import { routeFromHash, routeFromPath, routePath } from './app/navigation'
import { HomePage } from './pages/HomePage'

async function start() {
    // Old README links and bookmarks remain usable, including hash changes
    // made after the page has loaded.
    const migrateHash = () => {
        if (!window.location.hash.startsWith('#/')) return false
        const route = routeFromHash(window.location.hash)
        window.location.replace(
            routePath(route.page, route.section || undefined),
        )
        return true
    }
    window.addEventListener('hashchange', migrateHash)
    if (migrateHash()) return

    const route = routeFromPath(window.location.pathname)
    const content =
        route.page === 'docs' ? (
            import('./pages/DocsPage').then(({ DocsPage }) => (
                <DocsPage section={route.section} />
            ))
        ) : (
            <HomePage />
        )
    const root = document.getElementById('root')
    if (!root) throw new Error('Wyrd web root element was not found.')
    const app = (
        <StrictMode>
            <App route={route}>{await content}</App>
        </StrictMode>
    )
    if (root.childElementCount > 0) hydrateRoot(root, app)
    else createRoot(root).render(app)
}

void start()
