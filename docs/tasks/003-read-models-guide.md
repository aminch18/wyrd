# Task 003 — Guide: modelling read models

| | |
|---|---|
| Branch | `codex/read-models-guide`, from latest `origin/feature/v0.3-skuld` |
| Delivery | One pull request **into `feature/v0.3-skuld`** |
| Roadmap | Documentation & adoption — "Guide: modelling read models (projections) for screens and reports" |

Read first: `AGENTS.md`, `docs/design/semantics.md` (section 5), `docs/guides/philosophy.md`,
`docs/guides/from-ddd-to-wyrd.md` (match their tone and structure), `src/Wyrd/Slices/Projection.cs`,
`samples/Shop/Orders/OrderSummaries.cs`, `samples/Shop.Tests/OrdersScreenSpecs.cs`.

## Goal

`docs/guides/read-models.md`: how a team models the *read side* with Wyrd, for developers coming from "a table with a
Status column" and EF Core queries.

Cover, with examples from the shop:
- Write side vs read side: the state a slice decides on is not the screen; views are shaped for a screen or a report.
- `Projection<TView>`: pure `On<TEvent>(key, apply)`, returning null to delete, one projection per view, events from
  several slices feeding one view.
- Guarantees: the view is stored in the transaction that handles the event; a redelivered message is never applied twice.
- **Ordering, honestly**: today events from different streams can arrive in any order, so a view must tolerate it
  (the shop's "statuses only move forward", found by Skuld); ordered delivery per stream is *planned in v0.3* — say so
  and keep the advice valid either way.
- Testing views with `Scenario.For(projection).Given(...).Then(key, view)`, including a reordering test.
- Querying: `View<TView>(key)`, `Views<TView>()`, and `IWyrdRuntime` in endpoints.
- Common mistakes: deciding on a view, putting logic in endpoints, views that assume order, one huge view for everything.

## Bounds

- In: `docs/guides/read-models.md` (new), a link from `docs/guides/from-ddd-to-wyrd.md` and from `README.md`'s
  documentation links, your item in `docs/ROADMAP.md`.
- Out: all code, `src/Wyrd.Web/**`, other docs. Never describe a planned feature as available (check
  `docs/design/semantics.md` markers).

## Acceptance

- [ ] Every code sample compiles against the current API (copy from the sample where possible).
- [ ] Planned behaviour is clearly marked as planned.
- [ ] PR description summarises the guide's structure.
