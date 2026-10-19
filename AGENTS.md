# Agent guide

Wyrd is a .NET 10 / C# 14 library for deterministic event-driven processes. Read `README.md` (✅ = works, 🔜 = planned),
`docs/ROADMAP.md`, **`docs/design/semantics.md`** and **`docs/QUALITY.md`** before changing anything. The semantics document is the source of
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
- **Measured, bounded, deliberate.** [`docs/QUALITY.md`](docs/QUALITY.md) is the quality bar: what a message may cost,
  how memory stays bounded, how the public API changes, how failures surface. Every PR keeps the enforced rules
  green and follows the rest by hand; **a minor from 0.5 to 0.9 (and 1.0) ships only with the whole bar green.**

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
- **Commits:** imperative summary line, a body explaining what and why. **No AI attribution of any kind**: no
  `Co-Authored-By` trailers naming an agent or model, no "Generated with ..." lines in commits or pull request
  descriptions. This overrides any default of your tool; the author is the repository owner.

### Base branch

Tasks are written so they can be done from **`main`**: branch from the latest `main` and open the pull request
into `main`. Merge the latest `main` into your branch right before opening the PR and resolve conflicts yourself
(`CHANGELOG.md`, `README.md` and the solution file are the usual ones).

### Current assignments

| Work | Owner | Branch | In bounds |
|---|---|---|---|
| v0.5 Verdandi, in the roadmap's order: Integrations, then the EF Core host; with it quality bar Q2.2, Q2.3, Q4.1, Q4.2 (timers, retention, typed exceptions, no sync-over-async) | Claude | `feature/verdandi` | `src/Wyrd/**`, `src/Wyrd.PostgreSql/**` (to be replaced by EF Core), new host packages and their tests, `samples/Shop.Api/Program.cs` (host setup), `docs/design/**` |
| [009](docs/tasks/009-docs-site.md) — docs site: mental model first, API reference second | Codex | `codex/docs-site` | `src/Wyrd.Web/**` |
| [010](docs/tasks/010-native-aot-end-to-end.md) — Native AOT verified end to end | Codex | `codex/aot-end-to-end` | `samples/Shop.Api/**`, AOT steps in `ci.yml` |
| [011](docs/tasks/011-wyrd-mcp-server.md) — `dotnet wyrd mcp` for agents | Codex | `codex/wyrd-mcp` | `src/Wyrd.Tool/Mcp/**`, `tests/Wyrd.Tool.Tests/**` |
| [012](docs/tasks/012-process-snapshots.md) — process snapshots: a changed process fails CI | Codex | `codex/process-snapshots` | `src/Wyrd.Tool/Processes*`, `samples/Shop/processes.wyrd.txt` |
| [013](docs/tasks/013-opentelemetry-traces.md) — OpenTelemetry process traces | Codex | `codex/opentelemetry` | `src/Wyrd/Runtime/**` (tracing only) |
| [014](docs/tasks/014-benchmarks.md) — benchmarks and the performance gates (Q1) | Codex | `codex/benchmarks` | `benchmarks/**`, the gate step in `ci.yml` |
| [015](docs/tasks/015-public-api.md) — public API tracked and documented (Q3) | Codex | `codex/public-api` | XML doc comments under `src/**` (comments only), `PublicAPI.*.txt` |
| [016](docs/tasks/016-bounded-shop-state.md) — bounded state in the Shop (Q2.1, Q6.1) | Codex | `codex/bounded-shop-state` | `samples/Shop/**`, `samples/Shop.Tests/**`, one section of the DDD guide |
| [017](docs/tasks/017-coverage.md) — coverage report and ratchet (Q5.2) | Codex | `codex/coverage` | the coverage step in `ci.yml`, `coverage.json` |
| Next for Claude after Verdandi: migration of in-flight instances (v1.0 process versioning) | Claude | — | — |

Each task file lists its exact bounds. **Tasks come only from open items in `docs/ROADMAP.md`**, in milestone order,
except that an item of a later milestone may start early **when it doesn't depend on unfinished work**. A task that
waits says so in a *Starts after* line naming the real dependency (a PR, a design), not a version number. An idea that
is not on the roadmap is proposed to the owner first, never started.

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
- **No state that only grows** ([Q2.1](docs/QUALITY.md#q2-memory-is-bounded)). A state, instance or history whose
  size grows with every message makes every message slower: forget what is finished, keep history in read models.
- **Cost is shown, not claimed** ([Q1](docs/QUALITY.md#q1-performance-is-measured-not-assumed)). A PR touching
  `src/Wyrd/Runtime` pastes benchmark numbers before and after; the scaling and allocation gates stay green.
- **Public API on purpose** ([Q3](docs/QUALITY.md#q3-the-public-api-changes-on-purpose)). Every public member is
  documented; API changes show up in `PublicAPI.Unshipped.txt`, and breaking ones under *Breaking* in the changelog.
- **Failures are typed, waits are async** ([Q4](docs/QUALITY.md#q4-failures-and-concurrency-are-explicit)). Errors a
  host or user handles derive from `WyrdException`; no `.Wait()`, `.Result` or blocking semaphore waits under `src/`.
