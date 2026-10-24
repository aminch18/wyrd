import assert from 'node:assert/strict'
import { readFile, access } from 'node:fs/promises'
import { test } from 'node:test'
import { DOC_SECTIONS } from '../src/data/docs.ts'
import { SITE_URL } from '../src/app/seo.ts'

test('every sitemap page serves unique metadata, content and crawlable links without JavaScript', async () => {
    const sitemap = await readFile('dist/sitemap.xml', 'utf8')
    const urls = [...sitemap.matchAll(/<loc>([^<]+)<\/loc>/g)].map(
        (match) => match[1],
    )
    assert.deepEqual(urls, [
        SITE_URL,
        ...DOC_SECTIONS.map(({ id }) => `${SITE_URL}docs/${id}/`),
    ])
    const titles = new Set()
    const descriptions = new Set()
    for (const url of urls) {
        const file = `dist/${url.slice(SITE_URL.length)}index.html`
        const html = await readFile(file, 'utf8')
        assert.equal(
            [...html.matchAll(/<link rel="canonical"/g)].length,
            1,
            file,
        )
        assert.ok(html.includes(`<link rel="canonical" href="${url}"`), file)
        assert.equal([...html.matchAll(/<h1[\s>]/g)].length, 1, file)
        assert.match(html, /<main[\s>]/, file)
        assert.doesNotMatch(
            html,
            /<!--page-(?:head|body)-->|Loading documentation|href="#\/docs/,
            file,
        )
        const title = /<title>([^<]+)<\/title>/.exec(html)?.[1]
        const description = /name="description" content="([^"]+)"/.exec(
            html,
        )?.[1]
        assert.ok(title && description, file)
        assert.ok(!titles.has(title), file)
        assert.ok(!descriptions.has(description), file)
        titles.add(title)
        descriptions.add(description)
        const schema = JSON.parse(
            /<script type="application\/ld\+json">([^<]+)<\/script>/.exec(
                html,
            )[1],
        )
        assert.equal(schema['@context'], 'https://schema.org')
        assert.equal(schema['@graph'][1].url, url)
        assert.match(
            html,
            /property="og:image" content="https:\/\/aminch18.github.io\/wyrd\//,
        )
        assert.match(html, /name="twitter:card"/)
        for (const [, href] of html.matchAll(
            /(?:href|src)="(\/wyrd\/[^"#?]*)/g,
        )) {
            const relative = href.slice('/wyrd/'.length)
            await access(
                `dist/${relative}${href.endsWith('/') ? 'index.html' : ''}`,
            )
        }
        if (url !== SITE_URL) {
            assert.match(html, /<article[\s>]/)
            assert.equal(schema['@graph'][2]['@type'], 'BreadcrumbList')
        }
    }
    const reference = await readFile(
        'dist/docs/api-reference/index.html',
        'utf8',
    )
    assert.match(reference, /ResumeAsync/)
    assert.match(reference, /WYRD0300/)
    assert.match(reference, /<pre[\s>]/)
    const errorPage = await readFile('dist/404.html', 'utf8')
    assert.match(errorPage, /name="robots" content="noindex"/)
})
