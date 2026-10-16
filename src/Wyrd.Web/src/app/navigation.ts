export type Page = 'home' | 'docs'

export interface Route {
    page: Page
    section: string
}

export function routeFromHash(hash: string): Route {
    if (!hash.startsWith('#/docs')) {
        return { page: 'home', section: '' }
    }

    return { page: 'docs', section: hash.split('/')[2] ?? 'quick-start' }
}

export function navigate(page: Page, section?: string) {
    const suffix = page === 'docs' && section ? `/${section}` : ''
    window.location.hash = page === 'home' ? '#/' : `#/docs${suffix}`
}
