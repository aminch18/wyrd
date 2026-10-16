# Agent guide

Wyrd is a .NET 10 / C# 14 library for deterministic event-driven processes. Read `README.md` (✅ = works, 🔜 = planned) and
`docs/ROADMAP.md` before changing anything.

## Layout

- `src/Wyrd` — the library. `Processes/` (model + fluent), `Pipe/` (opt-in syntax), `Runtime/` (in-memory runtime).
- `tests/Wyrd.Tests` — unit and runtime tests. Runtime behaviour is tested through `InMemoryRuntime`.
- `samples/Shop` — runnable vertical-slice sample; CI runs it as a smoke test.
- Package versions live in `Directory.Packages.props`; shared settings in the `Directory.Build.props` files.

## Commands

```bash
dotnet build   # warnings are errors
dotnet test
dotnet run --project samples/Shop
```

## Principles

- **Pure core, framework-owned effects.** User-facing deciders and steps are pure functions. Never add APIs that
  let user code publish, persist or schedule directly.
- **One model, two syntaxes.** Fluent (`ProcessBuilder`) and pipe (`Wyrd.Pipe`) must build an identical `Process`.
  Put shared behaviour in `NodeModifiers`; `fluent_and_pipe_build_the_same_process` must keep passing.
- **The process is data.** New process features become new node data first, then rendering/simulation/runtime.
- **AOT-friendly.** No runtime code generation; prefer source generators.
- **Readable over clever.** If a junior .NET dev can't read the user-facing API, it's too clever.

## Conventions

- Repository content is in English.
- Keep `CHANGELOG.md` (Unreleased) up to date.
- The runtime must stay deterministic: no wall-clock time, no randomness, no unordered iteration that changes the journal.
- File-scoped namespaces, immutable types, records for data.
- Tests: xUnit, `snake_case` method names describing behaviour.
