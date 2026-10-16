# Agent guide

Wyrd is a .NET 10 / C# 14 library for deterministic event-driven processes. Read `README.md` (✅ = works, 🔜 = planned) and
`docs/ROADMAP.md` before changing anything.

## Layout

- `src/Wyrd` — the library. `Processes/` (model + fluent), `Pipe/` (opt-in syntax), `Runtime/`:
  `Engine` handles one `Envelope` at a time and returns `Changes` committed atomically through `IWyrdStore`;
  `InMemoryRuntime` is a host (queue + `InMemoryStore` + virtual clock). Future hosts (PostgreSQL, RabbitMQ)
  drive the same engine — keep it free of transport and storage details.
- `src/Wyrd.Analyzers` — Roslyn analyzers (netstandard2.0, Roslyn 4.14), packed into the Wyrd package.
  New rules go in `Diagnostics.cs` **and** `AnalyzerReleases.Unshipped.md`. Messages: what, why, how to fix.
- `src/Wyrd.Analyzers.CodeFixes` — IDE code fixes (needs Workspaces, so it can't live in the analyzer assembly).
- `src/Wyrd.Testing` — Given/When/Then scenarios (uses Wyrd internals through InternalsVisibleTo).
- `tests/Wyrd.Tests` — unit and runtime tests. Runtime behaviour is tested through `InMemoryRuntime`.
- `tests/Wyrd.Analyzers.Tests` — compile a snippet with `AnalyzerHarness.Analyze(source, contracts)`.
- `samples/Shop` — the sample domain (library) with its `contracts.wyrd.txt`; hosted by `samples/Shop.Api`
  (minimal API + `.http` file) and `samples/Shop.Console` (CI runs it as a smoke test).
- Package versions live in `Directory.Packages.props`; shared settings in the `Directory.Build.props` files.

## Commands

```bash
dotnet build   # warnings are errors
dotnet test
dotnet run --project samples/Shop.Console
dotnet run --project samples/Shop.Api   # http://localhost:5080, see Shop.Api.http
```

## Principles

- **Pure core, framework-owned effects.** User-facing deciders and steps are pure functions. Never add APIs that
  let user code publish, persist or schedule directly.
- **One model, two syntaxes.** Fluent (`ProcessBuilder`) and pipe (`Wyrd.Pipe`) must build an identical `Process`.
  Put shared behaviour in `NodeModifiers`; `fluent_and_pipe_build_the_same_process` must keep passing.
- **Commands, domain events and integration events are different things.** Never blur them: commands are
  sent to one owner, domain events stay internal, integration events are versioned public contracts.
- **The process is data.** New process features become new node data first, then rendering/simulation/runtime.
- **AOT-friendly.** No runtime code generation, no `Type.GetType`: types are resolved by registered name and
  serialized with `JsonTypeInfo` from the app's `[WyrdJsonContext]`. Reflection lives only behind
  `[RequiresUnreferencedCode]` entry points. CI publishes `samples/Shop.Console` with Native AOT.
- **Readable over clever.** If a junior .NET dev can't read the user-facing API, it's too clever.

## Conventions

- Repository content is in English.
- Keep `CHANGELOG.md` (Unreleased) up to date.
- The runtime must stay deterministic: no wall-clock time, no randomness, no unordered iteration that changes the journal.
- File-scoped namespaces, immutable types, records for data.
- Tests: xUnit, `snake_case` method names describing behaviour.

## Working in parallel

Several agents may work on this repository at the same time. To stay out of each other's way:

- **Never commit to `main`.** Work on a branch (`<agent>/<topic>`, e.g. `codex/wyrd-tool`, `feature/skuld`) and open
  a pull request. CI must be green; one concern per PR.
- **Use your own working copy.** If another agent is using a folder, create a `git worktree` for your branch.
- **Stay in your scope.** Tasks live in `docs/tasks/`, each with its files in and out of bounds. If you need to change
  something outside your scope, don't: say so in the PR.
- **Shared files:** in `CHANGELOG.md` add your lines under *Unreleased* only; in `docs/ROADMAP.md` tick only your
  items; in `README.md` add or edit only your own section. Rebase on `main` before opening the PR.
- **Commits:** imperative summary line, a body explaining what and why. No AI attribution trailers.

### Current assignments

| Work | Owner | Branch | In bounds |
|---|---|---|---|
| Skuld — simulation (v0.3) | Claude | `feature/skuld` | `src/Wyrd/**`, `src/Wyrd.Testing/**`, `tests/Wyrd.Tests/**`, `samples/**` |
| [Task 001](docs/tasks/001-wyrd-tool.md) — `dotnet wyrd` tool | Codex | `codex/wyrd-tool` | `src/Wyrd.Tool/**`, `tests/Wyrd.Tool.Tests/**`, generated sample docs |
