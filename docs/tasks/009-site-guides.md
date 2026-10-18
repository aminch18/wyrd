# Task 009 — Site: guides and analyzer pages up to date

| | |
|---|---|
| Branch | `codex/site-guides` |

Read first: `AGENTS.md`, `src/Wyrd.Web/**` (how `philosophy.md` is rendered by `OverviewDoc` / `MarkdownArticle`),
`docs/guides/read-models.md`, `docs/design/semantics.md`, `CHANGELOG.md` [0.3.0].

## Goal

- Render `docs/guides/read-models.md` on the site (like the philosophy guide), in the docs navigation after
  "From DDD to Wyrd". If task 008 is merged, render `operations.md` too; otherwise leave a TODO in the PR.
- Analyzers page: WYRD0011–0014, WYRD0020 and the suppressor, the fluent↔pipe code fix (WYRD0013) and **Fix all** for
  contracts and JSON (WYRD0200/0202, WYRD0300–0302). Check every code against `src/Wyrd.Analyzers/Diagnostics.cs`.
- Relative links inside rendered guides must work on the site (point to GitHub for code files).
- Check the site at phone width (no horizontal scroll) and in light and dark themes; attach screenshots to the PR.

## Bounds

- In: `src/Wyrd.Web/**`.
- Out: the guides' Markdown (report problems in the PR instead), all .NET code.

## Acceptance

- [ ] `npm ci && npm run format:check && npm run build` pass; screenshots in the PR.
