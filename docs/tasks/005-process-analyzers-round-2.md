# Task 005 — Process analyzers, round 2: what a step reads

| | |
|---|---|
| Branch | `codex/process-analyzers-2` |
| Roadmap | v0.3 follow-up — compile-time version of rules the runtime checks today |

Read first: `AGENTS.md`, `docs/design/semantics.md` 3.2, `src/Wyrd/Runtime/Registry.cs` (`Validate`),
`src/Wyrd.Analyzers/ProcessAnalyzer.cs`, `ProcessChain.cs`, `tests/Wyrd.Analyzers.Tests/ProcessRuleTests.cs`,
`tests/Wyrd.Tests/ReadingEventsTests.cs`.

## Goal

Steps and undos may read events of earlier steps (`Ship(OrderPlaced order, PaymentCharged charged)`). The runtime
rejects a wrong read before the first message; the compiler should say it first.

- **WYRD0015** (warning): a step reads an event that no earlier step or wait in the chain produces. "Produces" =
  the `TEvent` of the slice that owns the earlier step's command (`Slice<TCommand, TState, TEvent, TError>`), or an
  earlier `Await<TEvent>`. Only report when every owner is resolvable in the compilation; otherwise stay silent
  (the runtime still checks).
- **WYRD0016** (warning): an undo reads an event produced by its own step or a later one (its step may never have
  run).
- Both syntaxes (fluent and pipe). Messages: what, why, how to fix (see existing rules).

## Bounds

- In: `src/Wyrd.Analyzers/**` (new rules in `Diagnostics.cs` and `AnalyzerReleases.Unshipped.md`), a new test file in
  `tests/Wyrd.Analyzers.Tests/`, your lines in `CHANGELOG.md`, the rules table in `README.md`.
- Out: `src/Wyrd/**`, `src/Wyrd.Testing/**`, `src/Wyrd.Tool/**`, `samples/**`.

## Acceptance

- [ ] No false positives on `samples/Shop` and the existing test snippets.
- [ ] Tests for both rules, both syntaxes, awaited events, union events (`TEvent` a base type) and unresolvable owners.
