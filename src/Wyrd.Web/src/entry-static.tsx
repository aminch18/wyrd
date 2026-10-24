import { renderToString } from 'react-dom/server'
import { App } from './App'
import { HomePage } from './pages/HomePage'
import { DocsPage } from './pages/DocsPage'
import { DOC_SECTIONS } from './data/docs'
import type { Route } from './app/navigation'
import { pageMetadata, renderMetadata } from './app/seo'

export const routes: Route[] = [
    { page: 'home', section: '' },
    ...DOC_SECTIONS.map(({ id }) => ({ page: 'docs' as const, section: id })),
]

export function render(route: Route) {
    return {
        metadata: pageMetadata(route),
        head: renderMetadata(route),
        body: renderToString(
            <App route={route}>
                {route.page === 'home' ? (
                    <HomePage />
                ) : (
                    <DocsPage section={route.section} />
                )}
            </App>,
        ),
    }
}
