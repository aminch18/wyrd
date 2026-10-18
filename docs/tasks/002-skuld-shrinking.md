# Task 002 — Skuld: shrink a broken future to the perturbations that matter

| | |
|---|---|
| Branch | `codex/skuld-shrinking`, from latest `origin/feature/v0.3-skuld` |
| Delivery | One pull request **into `feature/v0.3-skuld`**, CI green |
| Roadmap | v0.3 — "Shrinking: reduce a failing future to the fewest perturbations that still break the invariant" |

Read first: `AGENTS.md`, `docs/design/semantics.md`, `src/Wyrd.Testing/Skuld.cs`, `SimulationOptions.cs`,
`SimulationReport.cs`, `Invariant.cs`, `tests/Wyrd.Tests/SkuldTests.cs`.

## Goal

Today a broken future reports every perturbation Skuld injected — often a dozen — though usually one or two caused
the break. After a future breaks, Skuld should **shrink** it: find a smaller set of perturbations that still breaks
the same invariant, and report that set and its journal.

## Design (follow it unless you find a better one; explain any change in the PR)

- Skuld perturbs through the internal `ISimulation` hooks (`PickNext`, `Duplicate`, `Fault`). Record every decision of
  a future as a **decision stream**: one entry per hook call, with whether it perturbed (and the picked index for
  `PickNext`).
- A replay mode plays a stream back with some perturbations **turned off** (neutral answer: index 0, no duplicate, no
  fault). Entries are consumed in order; when the stream runs out, answer neutral. Removing a perturbation may shift
  later decision points — that's fine (the same approach as decision-stream shrinking in property-based testing);
  determinism is what matters.
- Greedy shrinking: try removing each perturbation (then pairs if cheap); keep a removal when the **same invariant**
  still breaks. Bound the work (e.g. `SimulationOptions.ShrinkBudget`, default 200 replays). Shrinking on by default
  (`SimulationOptions.Shrink = true`).
- `Violation` gains the shrunk result (perturbations and journal). The report shows the shrunk future first
  ("Smallest future that breaks it: …") and keeps the seed for exact replay of the original.

## Bounds

- In: `src/Wyrd.Testing/**`, new tests in `tests/Wyrd.Tests/` (a new file, e.g. `SkuldShrinkingTests.cs`), your lines
  under *Unreleased* in `CHANGELOG.md`, your item in `docs/ROADMAP.md`, a short paragraph in `README.md`'s Skuld section.
- Out (another agent works there): `src/Wyrd/**` (do **not** change `ISimulation` or the engine), `samples/**`,
  `src/Wyrd.Web/**`, other test files. If you need a change there, explain it in the PR instead.

## Acceptance

- [ ] `dotnet build` (warnings are errors) and `dotnet test` pass; CI green.
- [ ] Shrinking the `Fragile` process from `SkuldTests` reports **one** perturbation: the transient failure of the
      command that had no retries. Deterministic: same seed, same shrunk result.
- [ ] A passing simulation does no shrinking work; a broken one stays within the budget.
- [ ] Tests for: stream recording, neutral replay, greedy removal, budget, report content.
