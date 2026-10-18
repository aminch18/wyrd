# Task 012 — Process snapshots: a changed process fails CI

| | |
|---|---|
| Branch | `codex/process-snapshots` |
| Roadmap | v1.0 — "Process versioning: snapshot of every process; breaking changes fail the build until in-flight instances are migrated" (the snapshot and the failing check; migration comes later) |

Read first: `AGENTS.md` (review checklist), `docs/design/semantics.md` 3.1 (fingerprints), `Process.Fingerprint` in
`src/Wyrd/Processes/Process.cs`, `src/Wyrd.Tool/**` (`Diagrams` already loads an assembly and finds its public
static processes), `samples/Shop/contracts.wyrd.txt` and how WYRD0200–0204 treat a contract snapshot.

## Goal

Today a changed process structure is only discovered at runtime: in-flight instances suspend with "definition
changed". Move that discovery to the pull request, the way contract snapshots already do for integration events.

1. `dotnet wyrd processes --assembly Shop.dll --output processes.wyrd.txt` writes one block per process: its name,
   its fingerprint and its structure in readable lines (trigger, steps with undos, waits, outcome — exactly what the
   fingerprint hashes, nothing else). Sorted, stable, diff-friendly; a header comment explains the file.
2. `dotnet wyrd processes --assembly Shop.dll --check processes.wyrd.txt` exits 1 when a process's structure
   differs from the snapshot, printing which process, the old and new structure, and what to do (in-flight
   instances of that process will suspend; deploy when none are running, or keep the old definition until they
   finish; then regenerate the snapshot). Added and removed processes are reported too. Retry or time-limit changes
   are not breaking and must not fail the check.
3. Find processes with the same code as `diagram` (share it, don't copy it).
4. `samples/Shop/processes.wyrd.txt` committed; CI runs the check against the built Shop (ubuntu job, next to the
   other sample steps). Run `actionlint` before pushing.
5. A short section in `README.md` (your own) with output copied from a real run.

## Bounds

- In: `src/Wyrd.Tool/Processes*.cs`, `src/Wyrd.Tool/Commands/Processes*.cs`, one `AddCommand` registration in
  `src/Wyrd.Tool/Program.cs`, `src/Wyrd.Tool/Diagrams.cs` (only to share process discovery),
  `tests/Wyrd.Tool.Tests/**`, `samples/Shop/processes.wyrd.txt`, the CI step, your section of `README.md`, your
  lines in `CHANGELOG.md`.
- Out: `src/Wyrd/**` (if `Process` needs to expose its structure as text, say so in the PR; don't parse the
  fingerprint), `src/Wyrd.Tool/Mcp/**` and `Commands/Replay*` (other tasks work there). The roadmap item stays
  open until migration of in-flight instances exists; say so in the PR.

## Acceptance

- [ ] `dotnet build` and `dotnet test` green; tests cover unchanged, structure changed, only retries changed, added
      and removed processes.
- [ ] The PR shows a real run of `--check` failing after reordering two steps in a scratch copy of the Shop.
