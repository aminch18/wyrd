# Task 010 — Native AOT verified end to end

| | |
|---|---|
| Branch | `codex/aot-end-to-end` |
| Roadmap | v1.0 — "Native AOT verified end to end" |
| Starts | now: nothing it needs is unfinished. When Verdandi lands, its PR runs the same smoke script against the durable host |

Read first: `AGENTS.md` (review checklist), `samples/Shop.Api/**`, `samples/Shop.Console/**`, `samples/Shop/ShopJson.cs`,
`.github/workflows/ci.yml` (the existing "Native AOT smoke test" step).

## Goal

Today CI publishes `samples/Shop.Console` with Native AOT and runs it. That proves the engine, not a real host. Prove
the whole path a user would ship: **an HTTP API on Wyrd, published as a native executable, driven over HTTP.**

1. `samples/Shop.Api` publishes with `PublishAot=true` and **zero trim/AOT warnings** (warnings are errors). Use the
   minimal API request delegate generator and a source-generated `JsonSerializerContext` for the API's own types
   (requests, responses); replace anonymous response objects with named records. The domain's JSON stays in
   `ShopJson` (`[WyrdJsonContext]`); don't duplicate it.
2. A smoke script (`samples/Shop.Api/smoke.sh`, bash, `curl` only) that drives the same story as `Shop.Api.http`:
   restock, place an order, add/cancel lines, submit a draft, payment webhook, then asserts on `GET /orders/{id}`,
   `GET /stock/{sku}` and `GET /dev/published` (status codes and the fields that matter, with `jq`). It fails loudly
   on the first mismatch and prints what it got.
3. CI (ubuntu only, next to the console step): publish the API with Native AOT, start the native binary, wait until
   it answers, run the smoke script, stop it. Run `actionlint .github/workflows/*.yml` before pushing and check the
   run after pushing.
4. `dotnet run --project samples/Shop.Api` keeps working exactly as before (JIT, dev tools, `.http` file).

## Bounds

- In: `samples/Shop.Api/**`, `samples/Shop.Console/**` (only if needed), `.github/workflows/ci.yml` (the AOT steps
  only), the roadmap item in `docs/ROADMAP.md`, your lines in `CHANGELOG.md`.
- Out: `src/**`, `samples/Shop/**` (if the domain blocks AOT, say so in the PR instead of changing it).
- Note: the parked Verdandi branch also touches `samples/Shop.Api/Program.cs`; keep the endpoint code where it is
  (no reshuffling) so that merge stays small.

## Acceptance

- [ ] `dotnet build` and `dotnet test` green; `dotnet publish samples/Shop.Api -c Release` produces a native binary
      with no warnings.
- [ ] The CI run shows the native API answering the smoke script; the PR links that run.
- [ ] The PR lists every change made only for AOT and why it was needed.
