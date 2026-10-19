import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const root = fileURLToPath(new URL('../../../', import.meta.url))
const read = (file) => readFile(path.join(root, file), 'utf8')
const api = JSON.parse(await read('src/Wyrd.Web/src/data/reference/api.json'))
const rules = JSON.parse(
    await read('src/Wyrd.Web/src/data/reference/analyzers.json'),
)
const normalize = (text) => text.replace(/\s+/g, ' ').trim()
const sources = new Map()
const escapePattern = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
for (const entry of api) {
    const source = await read(entry.source)
    sources.set(entry.source, source)
    assert.ok(
        entry.description && entry.example && entry.signatures.length,
        `Incomplete entry: ${entry.name}`,
    )
    for (const signature of entry.signatures) {
        const indent = ['Pipeline', 'Literals'].includes(entry.name)
            ? '(?: {0}| {4}| {8})'
            : '(?: {0}| {4})'
        assert.ok(
            new RegExp(
                `^${indent}${escapePattern(signature.split('\n')[0])}`,
                'm',
            ).test(source),
            `Declaration is not a public type/member excerpt: ${entry.name}: ${signature}`,
        )

        assert.ok(
            normalize(source).includes(normalize(signature)),
            `Stale signature: ${entry.name}: ${signature}`,
        )
    }
}

async function sourceFiles(folder) {
    const entries = await readdir(path.join(root, folder), {
        withFileTypes: true,
    })
    const files = await Promise.all(
        entries
            .filter((entry) => !['obj', 'bin'].includes(entry.name))
            .map((entry) => {
                const file = `${folder}/${entry.name}`
                return entry.isDirectory()
                    ? sourceFiles(file)
                    : entry.name.endsWith('.cs')
                      ? [file]
                      : []
            }),
    )
    return files.flat()
}

let types = 0
for (const file of [
    ...(await sourceFiles('src/Wyrd')),
    ...(await sourceFiles('src/Wyrd.Testing')),
]) {
    const source = await read(file)
    // Inventory of explicit top-level public declarations, not generated record members.
    for (const [declaration] of source.matchAll(
        /^public .*?\b(?:class|interface|record(?: struct)?|enum)\s+\w+[^\n]*/gm,
    )) {
        assert.ok(
            api.some(
                (entry) =>
                    entry.source === file &&
                    normalize(entry.signatures[0]).startsWith(
                        normalize(declaration),
                    ),
            ),
            `Unindexed public type: ${file}: ${declaration}`,
        )
        types++
    }
}

for (const file of [
    'src/Wyrd/Processes/ProcessBuilder.cs',
    'src/Wyrd/Pipe/Pipeline.cs',
    'src/Wyrd/Runtime/InMemoryRuntime.cs',
]) {
    const source = sources.get(file)
    const signatures = api
        .filter((entry) => entry.source === file)
        .flatMap((entry) => entry.signatures)
    for (const [line] of source.matchAll(/^\s+public (?!override )[^\n]+/gm)) {
        const header = normalize(line).split(' =>')[0].split(' {')[0]
        assert.ok(
            signatures.some((signature) =>
                normalize(signature).startsWith(header),
            ),
            `Unindexed builder/runtime member: ${file}: ${header}`,
        )
    }
}

const diagnostics = await read('src/Wyrd.Analyzers/Diagnostics.cs')
const declared = [...diagnostics.matchAll(/"(WYRD\d{4})",/g)]
    .map((match) => match[1])
    .sort()
assert.deepEqual(
    rules.map((rule) => rule.code).sort(),
    declared,
    'Analyzer coverage differs from Diagnostics.cs',
)
for (const rule of rules) {
    assert.ok(
        diagnostics.includes(`"${rule.title}"`),
        `Stale diagnostic title: ${rule.code}`,
    )
    const block = diagnostics
        .slice(diagnostics.indexOf(`"${rule.code}"`))
        .split('public static readonly')[0]
    assert.ok(
        block.includes(`DiagnosticSeverity.${rule.severity}`),
        `Stale diagnostic severity: ${rule.code}`,
    )
}
for (const name of [
    'ProcessBuilder',
    'Fragment',
    'Pipeline',
    'InMemoryRuntime',
]) {
    const entry = api.find((item) => item.name === name)
    assert.deepEqual(
        entry.members.map((member) => member.signature),
        entry.signatures.slice(1),
        `Missing per-member guidance/example: ${name}`,
    )
}
const page = await read('src/Wyrd.Web/src/pages/docs/ApiReferenceDoc.tsx')
for (const file of [
    'UnionSwitchSuppressor.cs',
    'ProcessStyleCodeFix.cs',
    'ContractSnapshotCodeFix.cs',
    'JsonContextCodeFix.cs',
]) {
    assert.ok(page.includes(file), `Missing suppressor/code-fix link: ${file}`)
    await read(
        `src/${file === 'UnionSwitchSuppressor.cs' ? 'Wyrd.Analyzers' : 'Wyrd.Analyzers.CodeFixes'}/${file}`,
    )
}
console.log(
    `Reference checked: ${types} public types, fluent/pipe/runtime members, ${rules.length} diagnostics, suppressor and 3 code-fix providers.`,
)
