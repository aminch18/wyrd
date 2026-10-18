# Task 010 — Benchmarks

| | |
|---|---|
| Branch | `codex/benchmarks` |

Read first: `AGENTS.md`, `Directory.Packages.props`, `samples/Shop/ShopModule.cs`, `samples/Shop.Tests/SkuldSpecs.cs`.

## Goal

Numbers for the README and a guard against regressions, with BenchmarkDotNet:

- `benchmarks/Wyrd.Benchmarks` (console, not packed, not run by `dotnet test`): commands decided per second (one
  slice), orders fulfilled end to end per second (Shop process, in memory), Skuld futures explored per second
  (Shop scenario), JSON round trip with the source-generated context vs reflection.
- Memory diagnoser on; short job by default, `--job default` documented for real runs.
- A `docs/benchmarks.md` with how to run them and the machine-independent conclusions only (no absolute claims).
- CI: build the project (so it doesn't rot) but don't run it.

## Bounds

- In: `benchmarks/**` (new), the solution file, `Directory.Packages.props` (BenchmarkDotNet only), a build step in
  `.github/workflows/ci.yml` (run `actionlint` before pushing), `docs/benchmarks.md`.
- Out: `src/**`, `samples/**`.

## Acceptance

- [ ] `dotnet run -c Release --project benchmarks/Wyrd.Benchmarks -- --filter *` runs locally; results in the PR.
