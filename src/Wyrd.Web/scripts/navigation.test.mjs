import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
    routeFromHash,
    routeFromPath,
    routePath,
} from '../src/app/navigation.ts'
import { pageMetadata, renderMetadata } from '../src/app/seo.ts'

test('clean URLs round-trip at the production and development bases', () => {
    for (const base of ['/', '/wyrd/']) {
        assert.deepEqual(
            routeFromPath(routePath('docs', 'quick-start', base), base),
            { page: 'docs', section: 'quick-start' },
        )
        assert.deepEqual(routeFromPath(base, base), {
            page: 'home',
            section: '',
        })
        assert.equal(
            routePath('docs', undefined, base),
            `${base}docs/overview/`,
        )
    }
})
test('legacy hash URLs resolve to the matching documentation chapter', () => {
    assert.deepEqual(routeFromHash('#/docs'), {
        page: 'docs',
        section: 'overview',
    })
    assert.equal(
        routePath('docs', routeFromHash('#/docs/processes').section),
        '/wyrd/docs/processes/',
    )
    assert.deepEqual(routeFromHash('#/'), { page: 'home', section: '' })
})
test('metadata rejects unknown chapters and escapes HTML', () => {
    assert.throws(
        () => pageMetadata({ page: 'docs', section: 'missing' }),
        /Unknown documentation/,
    )
    assert.match(
        renderMetadata({ page: 'docs', section: 'messages' }),
        /Messages &amp; Slices/,
    )
})
