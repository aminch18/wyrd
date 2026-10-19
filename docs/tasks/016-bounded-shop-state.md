# Task 016 — Bounded state in the Shop

| | |
|---|---|
| Branch | `codex/bounded-shop-state` |
| Roadmap | Quality bar — "The Shop's stock forgets shipped orders; the guides teach bounded state" ([QUALITY.md](../QUALITY.md) Q2.1, Q6.1) |
| Starts | now |

Read first: `AGENTS.md` (review checklist), [`docs/QUALITY.md`](../QUALITY.md) (baseline and Q2.1),
`samples/Shop/Inventory/**`, `samples/Shop/Fulfillment/OrderFulfillment.cs`, `samples/Shop.Tests/**`,
`docs/guides/from-ddd-to-wyrd.md`.

## Goal

`StockItem.Reservations` keeps every order that ever reserved stock: shipping doesn't remove it. State is a JSON
snapshot, so one SKU's state grows with every order and each order costs more than the last (1,197 → 193 orders/s
over 20,000 orders, see the baseline). The sample teaches the mistake; fix it and teach the rule.

1. When an order ships, its reservation leaves the stock state (the units are gone for good, they are not returned
   to `Available`). Model it the Wyrd way: decide which slice owns it and which event says so; if the process gains a
   step, it declares `.Retry(...)` and `.Undo(...)` / `.NoCompensation("why")` like every other step. Keep releases
   idempotent and harmless when there is nothing to undo, as today.
2. Business specifications for the new rule (Given/When/Then), and a test that drives many orders through one SKU and
   asserts the stock state's reservations hold only orders in flight.
3. Skuld specs stay green; extend an invariant if the new step can break one.
4. The DDD guide gets a short section, **"State that only grows"**: why a snapshot with an ever-growing collection
   makes every message slower, how to spot it, and what to do (forget what is finished; move history to a read model).
   Link to the Shop's code.
5. If the fulfillment process structure changes and task 012's `processes.wyrd.txt` is already on `main`,
   regenerate it in this PR.

## Bounds

- In: `samples/Shop/**` (except `contracts.wyrd.txt` unless a contract really changes), `samples/Shop.Tests/**`,
  `samples/Shop.Console/**` (only if its story or numbers change), `docs/guides/from-ddd-to-wyrd.md` (the new
  section), QUALITY.md marker for Q6.1 once task 014's gate passes on the Shop, the roadmap item,
  your lines in `CHANGELOG.md`.
- Out: `src/**`, `samples/Shop.Api/**` (task 010 works there).

## Acceptance

- [ ] `dotnet test` green including `SkuldSpecs`; the Native AOT console runs.
- [ ] The PR shows real numbers before and after: orders/s and KB allocated per order over 20,000 orders on one SKU
      (task 014's benchmarks if merged, otherwise a harness included in the PR description).
