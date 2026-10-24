import { mkdir, readFile, writeFile, rm } from 'node:fs/promises'
import { routes, render } from '../.static/entry-static.js'

const template = await readFile('dist/index.html', 'utf8')
const urls = []
for (const route of routes) {
    const { metadata, head, body } = render(route)
    const directory =
        route.page === 'home' ? 'dist' : `dist/docs/${route.section}`
    await mkdir(directory, { recursive: true })
    await writeFile(
        `${directory}/index.html`,
        template
            .replace('<!--page-head-->', () => head)
            .replace('<!--page-body-->', () => body),
    )
    urls.push(metadata.url)
}
await writeFile(
    'dist/sitemap.xml',
    `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls.map((url) => `  <url><loc>${url}</loc></url>`).join('\n')}\n</urlset>\n`,
)
// robots.txt must be served at the origin root. For project Pages this copy is
// informational; configure the account-level site if root directives are needed.
await writeFile(
    'dist/robots.txt',
    'User-agent: *\nAllow: /wyrd/\nSitemap: https://aminch18.github.io/wyrd/sitemap.xml\n',
)
await writeFile('dist/.nojekyll', '')
await writeFile(
    'dist/404.html',
    '<!doctype html><html lang="en"><meta charset="utf-8"><meta name="robots" content="noindex"><title>Page not found — Wyrd</title><h1>Page not found</h1><p><a href="/wyrd/">Return to Wyrd</a></p></html>',
)
await rm('.static', { recursive: true, force: true })
console.log(`Prerendered ${urls.length} indexable pages.`)
