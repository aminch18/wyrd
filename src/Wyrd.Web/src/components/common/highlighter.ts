import { createHighlighterCore } from 'shiki/core'
import { createJavaScriptRegexEngine } from 'shiki/engine/javascript'
import csharp from 'shiki/langs/csharp.mjs'
import shellscript from 'shiki/langs/shellscript.mjs'
import yaml from 'shiki/langs/yaml.mjs'
import darkPlus from 'shiki/themes/dark-plus.mjs'
import type { ThemeRegistration } from 'shiki'

const darkModern: ThemeRegistration = {
    ...darkPlus,
    name: 'dark-modern',
    colors: {
        ...darkPlus.colors,
        'editor.background': '#1F1F1F',
        'editor.foreground': '#CCCCCC',
    },
}

// A TextMate translation of the Rider Islands Dark palette in the reference screenshots.
// Semantic C# classifications from Rider's language server are not available in a static site.
const rider: ThemeRegistration = {
    name: 'wyrd-rider',
    type: 'dark',
    colors: { 'editor.background': '#191A1C', 'editor.foreground': '#BDBDBD' },
    tokenColors: [
        {
            scope: ['comment'],
            settings: { foreground: '#85C46C', fontStyle: 'italic' },
        },
        { scope: ['keyword', 'storage'], settings: { foreground: '#6C95EB' } },
        { scope: ['string'], settings: { foreground: '#C9A26D' } },
        { scope: ['constant.numeric'], settings: { foreground: '#ED94C0' } },
        {
            scope: [
                'entity.name.type',
                'entity.name.namespace',
                'support.type',
                'support.class',
                'storage.type.generic',
            ],
            settings: { foreground: '#C191FF' },
        },
        {
            scope: ['entity.name.function', 'support.function'],
            settings: { foreground: '#39CC9B' },
        },
        {
            scope: [
                'variable.other.property',
                'variable.other.object.property',
                'variable.other.constant',
                'constant.other',
            ],
            settings: { foreground: '#66C3CC' },
        },
        {
            scope: ['variable.parameter', 'variable.other.readwrite'],
            settings: { foreground: '#BDBDBD' },
        },
        {
            scope: ['keyword.operator', 'punctuation'],
            settings: { foreground: '#BDBDBD' },
        },
        {
            scope: ['entity.other.attribute-name', 'variable.other.member'],
            settings: { foreground: '#66C3CC' },
        },
        {
            scope: ['entity.name.command', 'support.function.builtin'],
            settings: { foreground: '#39CC9B' },
        },
        {
            scope: ['string.unquoted.argument'],
            settings: { foreground: '#C9A26D' },
        },
    ],
}

const highlighter = createHighlighterCore({
    themes: [darkModern, rider],
    langs: [csharp, shellscript, yaml],
    engine: createJavaScriptRegexEngine(),
})

export async function highlight(code: string, language: string) {
    const instance = await highlighter
    return instance.codeToHtml(code, {
        lang: language,
        themes: { light: 'dark-modern', dark: 'wyrd-rider' },
    })
}
