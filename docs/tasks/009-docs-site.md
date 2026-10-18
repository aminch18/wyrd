# Task 009 — Docs site: mental model first, API reference second

| | |
|---|---|
| Branch | `codex/docs-site` |
| Roadmap | Documentation & adoption — "Docs site with the mental model first, API reference second" |

Read first: `AGENTS.md` (review checklist), `src/Wyrd.Web/**` (how `philosophy.md` is rendered by `OverviewDoc` /
`MarkdownArticle`), `docs/guides/*.md`, `docs/design/semantics.md`, `CHANGELOG.md` [0.3.0], the public API in
`src/Wyrd/**` and `src/Wyrd.Testing/**`.

## Goal

The site already opens with the mental model (philosophy, semantics, design decisions). Finish the roadmap item:

1. **Mental model first.** Render `docs/guides/read-models.md` on the site like the philosophy guide, after "From
   DDD to Wyrd". The navigation reads as a learning path: why → how it behaves → guides → reference.
2. **API reference second.** A reference section generated from or checked against the real public API: messages
   (`ICommand`, `IDomainEvent`, `IIntegrationEvent`, `[IntegrationEvent]`), slices (`Slice<...>`, `IState<T>`,
   `Result`, `[Union]`), processes (every builder method and modifier, fluent and pipe), runtime
   (`InMemoryRuntime` members, `ProcessStatus`, `ReasonOf`, `ResumeAsync`, `CompensateAsync`), projections,
   `Wyrd.Testing` (`Scenario`, `Skuld`, `SimulationOptions`, `Invariant`), and every analyzer rule (WYRD0001 to
   WYRD0302, the CS8509 suppressor, the code fixes including Fix all). Each entry: signature, one sentence, a short
   example, a link to the source on GitHub. No member that doesn't exist; nothing planned listed as available.
3. Relative links inside rendered guides work on the site (point to GitHub for code files).
4. Check at phone width (no horizontal scroll) and in light and dark themes; screenshots in the PR.

## Bounds

- In: `src/Wyrd.Web/**`, the roadmap item in `docs/ROADMAP.md`, your lines in `CHANGELOG.md`.
- Out: the guides' Markdown (report problems in the PR), all .NET code.

## Acceptance

- [ ] `npm ci && npm run format:check && npm run build` pass; screenshots in the PR.
- [ ] The PR lists how the reference was checked against the code (e.g. every public type in `src/Wyrd` covered).
