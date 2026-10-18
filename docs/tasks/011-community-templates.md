# Task 011 — Community: templates and contributor guide

| | |
|---|---|
| Branch | `codex/community` |

Read first: `AGENTS.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `docs/launch/checklist.md`.

## Goal

The repository is about to be announced. Make contributions easy and well-shaped:

- `.github/ISSUE_TEMPLATE/`: bug report (version, minimal repro, journal output), feature request (the business
  problem first), design question (points to Discussions); `config.yml` linking Discussions and the docs site.
- `.github/pull_request_template.md`: what and why, tests, docs/semantics updated, CHANGELOG line, no breaking change
  without a CHANGELOG "Breaking" note.
- `CONTRIBUTING.md`: how to build and test, the semantics document as the source of truth, how analyzers are added
  (Diagnostics.cs + AnalyzerReleases), commit style, and the "Working in parallel" rules for AI agents (link
  `AGENTS.md`).
- `SECURITY.md`: how to report a vulnerability privately (GitHub private advisories).

## Bounds

- In: `.github/ISSUE_TEMPLATE/**`, `.github/pull_request_template.md`, `CONTRIBUTING.md`, `SECURITY.md`.
- Out: workflows, code, `AGENTS.md`.

## Acceptance

- [ ] Templates render (check the YAML forms with the GitHub preview); links are absolute.
