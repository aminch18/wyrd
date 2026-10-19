# Task 017 — Coverage report and ratchet

| | |
|---|---|
| Branch | `codex/coverage` |
| Roadmap | Quality bar — "Coverage reported in CI, never lower than the previous release" ([QUALITY.md](../QUALITY.md) Q5.2) |
| Starts | now |

Read first: `AGENTS.md` (review checklist), [`docs/QUALITY.md`](../QUALITY.md) Q5, `.github/workflows/ci.yml`,
`tests/Directory.Build.props`, `docs/RELEASING.md`.

## Goal

Coverage is configured (`coverlet.collector`) but nobody sees it. Make it visible on every run and make it a floor.

1. CI (ubuntu job) collects coverage for `src/Wyrd` and `src/Wyrd.Testing` and writes line and branch coverage per
   package to the job summary (a table, with a link to the full HTML report as an artifact). Use an established tool
   (e.g. ReportGenerator), not a hand-rolled parser.
2. `coverage.json` at the repository root holds the floor: the numbers of the last release. The CI step fails when
   coverage drops below it, naming the package and both numbers. The first floor is the current `main`, from a real run.
3. `docs/RELEASING.md`: a release updates the floor to its own numbers (a ratchet: it only goes up).
4. Keep `dotnet test` fast: coverage runs once, in CI, not in every local run.

## Bounds

- In: `.github/workflows/ci.yml` (the coverage step), `coverage.json`, `tests/Directory.Build.props` (if needed),
  `Directory.Packages.props`, `docs/RELEASING.md` (one step), QUALITY.md marker for Q5.2, the roadmap item,
  your lines in `CHANGELOG.md`.
- Out: `src/**`, tests' content (finding untested code is a report in the PR, not a test-writing spree).

## Acceptance

- [ ] `actionlint` clean; the PR links a green CI run whose summary shows the coverage table.
- [ ] The PR lists the five least-covered public types in `src/Wyrd`, from the real report.
