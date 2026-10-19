# Task 014 — Benchmarks and the performance gates

| | |
|---|---|
| Branch | `codex/benchmarks` |
| Roadmap | Quality bar — "Benchmarks, with the scaling and allocation gates in CI" ([QUALITY.md](../QUALITY.md) Q1) |
| Starts | now. The single-SKU scaling check fails until task 016 bounds the Shop's stock state: that failure is the point. Merge 016 first, or land the check with that scenario marked as the known failure 016 removes. |

Read first: `AGENTS.md` (review checklist), [`docs/QUALITY.md`](../QUALITY.md) (the baseline section and Q1),
`samples/Shop/**`, `src/Wyrd/Runtime/InMemoryRuntime.cs`.

## Goal

Make cost visible on every PR. Today a change can make each order cost more than the last and every test still
passes; the baseline in QUALITY.md was found by hand.

1. `benchmarks/Wyrd.Benchmarks` (BenchmarkDotNet, `[MemoryDiagnoser]`) with the Q1.1 scenarios over the Shop:
   one SKU, one SKU per order, a compensated order (card declined), and timers (waits that end early, waits that
   time out, then `AdvanceAsync` past them). Report orders/s, messages/s and bytes allocated per order and per message.
2. A **fast mode** (`dotnet run -c Release --project benchmarks/Wyrd.Benchmarks -- --gate`) that doesn't use
   BenchmarkDotNet's timing: it runs each scenario at 1,000 and 10,000 orders, measures
   `GC.GetTotalAllocatedBytes(precise: true)` and checks
   - **Q1.2**: bytes per order at 10,000 ≤ 1.10 × bytes per order at 1,000;
   - **Q1.3**: bytes per message ≤ `benchmarks/baseline.json` + 10%.
   It prints a table and, on failure, which scenario, both numbers and the rule id. Exit code 1 on failure.
   Under 60 s in CI.
3. `benchmarks/baseline.json`, written by `--write-baseline` from a real run; the PR body shows that run.
4. CI (ubuntu job): run the gate after the tests. Run `actionlint` before pushing.
5. `benchmarks/README.md`: how to run both modes, how to read the output, when and how to update the baseline.

## Bounds

- In: `benchmarks/**`, `Wyrd.slnx`, `Directory.Packages.props` (BenchmarkDotNet), the gate step in `ci.yml`,
  QUALITY.md markers for Q1.1–Q1.3 (🔜 → ✅ when the gate runs in CI), the roadmap item, your lines in `CHANGELOG.md`.
- Out: `src/**`, `samples/**` (a sample problem the gate finds goes in the PR description, or into task 016).

## Acceptance

- [ ] `dotnet build` and `dotnet test` green; the gate runs in CI and the PR links a green run.
- [ ] The PR pastes the full BenchmarkDotNet results on the reference machine and the gate's table.
- [ ] Every number in the README and the PR comes from a real run.
