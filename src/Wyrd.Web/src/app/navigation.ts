export type Page = 'home' | 'docs'

export interface Route {
    page: Page
    section: string
}

// Also usable by Node tests outside Vite.
export const BASE_PATH = import.meta.env?.BASE_URL ?? '/wyrd/'

export function routePath(page: Page, section = 'overview', base = BASE_PATH) {
    return page === 'home' ? base : `${base}docs/${section}/`
}

export function routeFromHash(hash: string): Route {
    if (!hash.startsWith('#/docs')) return { page: 'home', section: '' }
    return { page: 'docs', section: hash.split('/')[2] || 'overview' }
}

export function routeFromPath(path: string, base = BASE_PATH): Route {
    const relative = path.startsWith(base) ? path.slice(base.length) : ''
    const match = /^docs\/([^/]+)\/(?:index\.html)?$/.exec(relative)
    return match
        ? { page: 'docs', section: match[1] }
        : { page: 'home', section: '' }
}

export function navigate(page: Page, section?: string) {
    window.location.assign(routePath(page, section))
}
