import { routePath } from '../../app/navigation.ts'
import { GITHUB_URL } from '../../app/links.ts'

const GUIDE_ROUTES: Record<string, string> = {
    'philosophy.md': routePath('docs', 'overview'),
    'from-ddd-to-wyrd.md': routePath('docs', 'from-ddd'),
    'read-models.md': routePath('docs', 'read-models'),
    'running-durable.md': routePath('docs', 'running-durable'),
    'how-verdandi-works.md': routePath('docs', 'how-verdandi-works'),
}

/** Resolve links against the Markdown file, rather than the deployed site URL. */
export function guideLink(url: string): string {
    if (/^(?:[a-z][a-z\d+.-]*:|\/\/|#\/)/i.test(url)) return url
    const source = new URL(url, `${GITHUB_URL}/blob/main/docs/guides/`)
    const guide = source.pathname.split('/').at(-1) ?? ''
    if (
        !source.hash &&
        !source.search &&
        source.pathname.includes('/docs/guides/')
    )
        return GUIDE_ROUTES[guide] ?? source.href
    return source.href
}
