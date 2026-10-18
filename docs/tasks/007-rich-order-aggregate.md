# Task 007 — A rich order in the Shop

| | |
|---|---|
| Branch | `codex/rich-order` |
| Roadmap | Documentation & adoption — "Rich aggregate in samples/Shop (Order, OrderLine, Money)" |

Read first: `AGENTS.md`, `docs/guides/from-ddd-to-wyrd.md`, `docs/guides/read-models.md`, `samples/Shop/**`,
`samples/Shop.Tests/**`.

## Goal

Readers coming from DDD ask "where is my aggregate with lines and money?". Show it the Wyrd way:

- `Money` (value object: amount + currency, no float, arithmetic that refuses mixed currencies), `OrderLine`.
- `Order` state as a `[Union]` with lines; slices `AddLine`, `CancelLine`, `CancelOrder` sharing invariants defined
  **on the state** (e.g. "a cancelled order can't change", "at most N lines", "total never negative"), never
  duplicated across slices.
- Business specifications in `Shop.Tests` (Given/When/Then), the read model updated, API endpoints in `Shop.Api`
  plus `.http` examples.
- Keep `PlaceOrder` and the fulfillment process working; Skuld specs must stay green.

## Bounds

- In: `samples/Shop/**` (except `Fulfillment/OrderFulfillment.cs` beyond what new events require),
  `samples/Shop.Tests/**`, `samples/Shop.Api/**`, `samples/Shop/contracts.wyrd.txt` (only if a contract really
  changes; prefer not), the DDD guide's links to the sample, your lines in `CHANGELOG.md`, your roadmap item.
- Out: `src/**`.

## Acceptance

- [ ] `dotnet test` green, including `SkuldSpecs`; Native AOT console still runs (`dotnet publish samples/Shop.Console -c Release`).
- [ ] Every invariant lives in one place; the PR description lists them and where they live.
