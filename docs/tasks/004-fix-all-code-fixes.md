# Task 004 — Code fixes that fix everything at once

| | |
|---|---|
| Branch | `codex/fix-all`, from latest `origin/feature/v0.3-skuld` |
| Delivery | One pull request **into `feature/v0.3-skuld`**, CI green |

Read first: `AGENTS.md`, `src/Wyrd.Analyzers.CodeFixes/*.cs`, `src/Wyrd.Analyzers/ContractAnalyzer.cs`,
`JsonContextAnalyzer.cs`, `tests/Wyrd.Analyzers.Tests/CodeFixTests.cs`, `JsonContextTests.cs`.

## Goal

Adopting Wyrd in an existing project raises many diagnostics at once — the shop needed 26 types in its JSON context
and 8 union cases. Today each fix is one click per diagnostic (`GetFixAllProvider` returns null). Make "Fix all in
document / project / solution" work:

- `ContractSnapshotCodeFix` (WYRD0200, WYRD0202): one fix-all writes every missing or outdated line into
  `contracts.wyrd.txt` in a single change (additional document; ordering stable, no duplicates).
- `JsonContextCodeFix` (WYRD0300, WYRD0301, WYRD0302): one fix-all adds every missing `[JsonSerializable]`,
  `[JsonDerivedType]` and unique `TypeInfoPropertyName` to the context and unions, in one change per document.
- Keep the single fixes exactly as they are.

## Bounds

- In: `src/Wyrd.Analyzers.CodeFixes/**`, new tests in `tests/Wyrd.Analyzers.Tests/` (new file), your lines under
  *Unreleased* in `CHANGELOG.md`.
- Out: `src/Wyrd.Analyzers/**` unless strictly needed for diagnostic properties (explain in the PR), all other code.
  Analyzer tests use `Microsoft.CodeAnalysis` 5.9 (VersionOverride) to compile C# 14; the analyzers target 4.14.

## Acceptance

- [ ] `dotnet build` and `dotnet test` pass; CI green.
- [ ] Tests: fix-all on a project with several missing contracts produces the full snapshot in one change; fix-all
      on a context missing several types and union cases ends with no WYRD03xx diagnostics.
