import assert from 'node:assert/strict'
import { readFile, access } from 'node:fs/promises'
import { test } from 'node:test'
import { guideLink } from '../src/pages/docs/guideLinks.ts'

const repository = 'https://github.com/aminch18/wyrd/blob/main/'
test('guide chapters stay in the learning path', () => {
    assert.equal(guideLink('read-models.md'), '#/docs/read-models')
    assert.equal(guideLink('./from-ddd-to-wyrd.md'), '#/docs/from-ddd')
    assert.equal(guideLink('philosophy.md'), '#/docs/overview')
})
test('relative source and design links resolve from docs/guides', () => {
    assert.equal(
        guideLink('../../samples/Shop/Orders/OrderSummaries.cs'),
        `${repository}samples/Shop/Orders/OrderSummaries.cs`,
    )
    assert.equal(
        guideLink('../design/semantics.md#5-read-models'),
        `${repository}docs/design/semantics.md#5-read-models`,
    )
    assert.equal(
        guideLink('../../tests/Wyrd.Tests/OrderedProjectionTests.cs'),
        `${repository}tests/Wyrd.Tests/OrderedProjectionTests.cs`,
    )
})
test('external links and site routes remain intact', () => {
    assert.equal(
        guideLink('https://learn.microsoft.com/example'),
        'https://learn.microsoft.com/example',
    )
    assert.equal(guideLink('#/docs/overview'), '#/docs/overview')
})
test('every relative guide link points to an existing repository file', async () => {
    for (const file of [
        'philosophy.md',
        'from-ddd-to-wyrd.md',
        'read-models.md',
    ]) {
        const guide = await readFile(
            new URL(`../../../docs/guides/${file}`, import.meta.url),
            'utf8',
        )
        for (const [, link] of guide.matchAll(/\]\(([^)]+)\)/g)) {
            if (/^(?:[a-z][a-z\d+.-]*:|\/\/)/i.test(link)) continue
            assert.match(
                guideLink(link),
                /^(?:#\/docs\/|https:\/\/github.com\/aminch18\/wyrd\/blob\/main\/)/,
            )
            await access(
                new URL(
                    link.split('#')[0],
                    new URL(`../../../docs/guides/${file}`, import.meta.url),
                ),
            )
        }
    }
})
