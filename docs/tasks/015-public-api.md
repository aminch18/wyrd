# Task 015 — The public API, tracked and documented

| | |
|---|---|
| Branch | `codex/public-api` |
| Roadmap | Quality bar — "Public API tracked and documented; package validation against the last release" ([QUALITY.md](../QUALITY.md) Q3) |
| Starts | now |

Read first: `AGENTS.md` (review checklist), [`docs/QUALITY.md`](../QUALITY.md) Q3, `src/Directory.Build.props`,
the public types of `src/Wyrd`, `src/Wyrd.Testing` and `src/Wyrd.Tool`, `CHANGELOG.md`.

## Goal

A reviewer must see in the diff when a PR changes the public API, and a user must be able to read what every public
member is for.

1. **Q3.1** Remove the CS1591 suppression (and its `TODO`) from `src/Directory.Build.props`, and document every public
   member it reveals (78 in `Wyrd` today): one or two sentences on what it does and when to use it, written for
   someone who doesn't know Wyrd; `<example>` where the use isn't obvious. Match the voice of the existing comments.
   Never describe planned behaviour as available (check [semantics.md](../design/semantics.md) markers).
2. **Q3.2** `Microsoft.CodeAnalysis.PublicApiAnalyzers` for every packable project, with `PublicAPI.Shipped.txt` holding
   the API of 0.4.0 and an empty `PublicAPI.Unshipped.txt`. Add a line to `docs/RELEASING.md`: a release moves
   Unshipped into Shipped.
3. **Q3.3** `PackageValidationBaselineVersion` = `0.4.0` for the packages published at 0.4.0, so `dotnet pack` reports
   any breaking change against it.
4. Add the matching line to the review checklist in `AGENTS.md`: public API changes show up in `PublicAPI.Unshipped.txt`
   and, when breaking, under *Breaking* in the changelog.

## Bounds

- In: XML documentation comments under `src/**` (**comments only**: no code changes, so other branches merge
  cleanly), `PublicAPI.*.txt` files, `src/Directory.Build.props`, `Directory.Packages.props` (one line),
  `docs/RELEASING.md` (one step), the review-checklist line in `AGENTS.md`, QUALITY.md markers for Q3,
  the roadmap item, your lines in `CHANGELOG.md`.
- Out: any change to code or signatures; if an API looks wrong while documenting it, list it in the PR.

## Acceptance

- [ ] `dotnet build` (warnings are errors), `dotnet test` and `dotnet pack` green with the suppression removed.
- [ ] The PR lists the members whose purpose was unclear enough to document with an example, and any API that looks wrong.
