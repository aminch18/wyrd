# Agent guide

Wyrd is a .NET 10 / C# 14 library for deterministic event-driven processes. Read `README.md` (✅ = works, 🔜 = planned),
`docs/ROADMAP.md` and **`docs/design/semantics.md`** before changing anything. The semantics document is the source of
truth for behaviour: if code and document disagree, fix the code, or change the document on purpose in the same PR.

## Layout

- `src/Wyrd` — the library. `Processes/` (model + fluent), `Pipe/` (opt-in syntax), `Runtime/`:
  `Engine` handles one `Envelope` at a time and returns `Changes` committed atomically through `IWyrdStore`;
  `InMemoryRuntime` is a host (queue + `InMemoryStore` + virtual clock). Future hosts (PostgreSQL, RabbitMQ)
  drive the same engine — keep it free of transport and storage details.
- `src/Wyrd.Analyzers` — Roslyn analyzers (netstandard2.0, Roslyn 4.14), packed into the Wyrd package.
  New rules go in `Diagnostics.cs` **and** `AnalyzerReleases.Unshipped.md`. Messages: what, why, how to fix.
- `src/Wyrd.Analyzers.CodeFixes` — IDE code fixes (needs Workspaces, so it can't live in the analyzer assembly).
- `src/Wyrd.Testing` — Given/When/Then scenarios and Skuld (uses Wyrd internals through InternalsVisibleTo:
  `ISimulation` hooks in `InMemoryRuntime` pick, duplicate and fault messages). Any engine change must keep
  `samples/Shop.Tests/SkuldSpecs` green: it explores thousands of hostile futures.
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
  items; in `README.md` add or edit only your own section. Bring in the latest base first (see below).
- **Commits:** imperative summary line, a body explaining what and why. No AI attribution trailers.

### Base branch

Branch from, and open pull requests into, **`main`** once release 0.3.0 is merged (PR #14); until then use
`feature/v0.3-skuld`. Merge the latest base into your branch right before opening the PR and resolve conflicts
yourself (`CHANGELOG.md`, `README.md` and the solution file are the usual ones).

### Current assignments

| Work | Owner | Branch | In bounds |
|---|---|---|---|
| v0.4 Urd: instance history capture, replay as a test | Claude | `feature/v0.4-urd` | `src/Wyrd/Runtime/**`, `src/Wyrd.Testing/**`, `src/Wyrd.Tool/**`, `docs/design/**` |
| Verdandi on PostgreSQL (parked until v0.4 ships) | Claude | `feature/verdandi` | `src/Wyrd.PostgreSql/**`, `tests/Wyrd.PostgreSql.Tests/**` |
| [005](docs/tasks/005-process-analyzers-round-2.md) — analyzers for what a step reads | Codex | `codex/process-analyzers-2` | `src/Wyrd.Analyzers/**` |
| [006](docs/tasks/006-richer-diagrams.md) — diagrams that show the decisions | Codex | `codex/richer-diagrams` | `ToMermaid` only |
| [007](docs/tasks/007-rich-order-aggregate.md) — rich order in the Shop | Codex | `codex/rich-order` | `samples/Shop*/**` |
| [008](docs/tasks/008-operations-guide.md) — operations guide | Codex | `codex/operations-guide` | `docs/guides/operations.md` |
| [009](docs/tasks/009-site-guides.md) — site: guides and analyzers | Codex | `codex/site-guides` | `src/Wyrd.Web/**` |
| [010](docs/tasks/010-benchmarks.md) — benchmarks | Codex | `codex/benchmarks` | `benchmarks/**` |
| [011](docs/tasks/011-community-templates.md) — community templates | Codex | `codex/community` | `.github/ISSUE_TEMPLATE/**`, `CONTRIBUTING.md` |
| [012](docs/tasks/012-booking-sample.md) — booking sample | Codex | `codex/booking-sample` | `samples/Booking*/**` |

Each task file lists its exact bounds. Tasks 007 and 012 both touch the solution file, and 005, 008 and 012 each
add a line to `README.md`: expect small conflicts and resolve them.

## Review checklist (learned from earlier pull requests)

Reviews check these, so check them first:

- **Idiomatic, not just correct.** Code reads like the surrounding code. In tools, use established libraries
  (Spectre.Console.Cli, YamlDotNet) instead of hand-rolled parsing; `src/Wyrd` itself stays dependency-free.
- **Never invent output.** Journal lines, reports and diagnostics quoted in docs, tests or the site come from a real
  run. User-facing text is exact, including singular and plural ("1 attempt", not "1 attempts").
- **Planned is never presented as available.** Check `docs/design/semantics.md` markers before describing behaviour;
  when you implement a rule, update its marker in the same PR.
- **Work proportional to what is shown.** Don't do expensive work for results nobody reads (e.g. Skuld shrinks only
  the first violation of each invariant, the one the report shows). Keep `dotnet test` fast.
- **Workflows are linted.** Run `actionlint .github/workflows/*.yml` before pushing a workflow change, and check the
  run after pushing: a YAML error fails every push before any job starts.
- **The README is packed into every NuGet package.** Links and images in `README.md` are absolute
  (`https://github.com/aminch18/wyrd/blob/main/...`), or they break on nuget.org.
- **Process snippets declare every decision.** Every step says `.Retry(...)` or `.NoRetry("why")`, every wait has
  `.Within(...)`, every step followed by others says `.Undo(...)` or `.NoCompensation("why")` — in docs and samples too.
- **Engine changes keep Skuld green.** `samples/Shop.Tests/SkuldSpecs` explores thousands of hostile futures; a
  failure there is a real bug, never a flaky test.
- **Native AOT.** Anything reachable from the runtime must publish with Native AOT (`dotnet publish
  samples/Shop.Console -c Release`, then run it); reflection only behind `[RequiresUnreferencedCode]` entry points.
