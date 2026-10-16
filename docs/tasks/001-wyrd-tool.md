# Task 001 — `Wyrd.Tool`: the `dotnet wyrd` command-line tool

| | |
|---|---|
| Branch | `codex/wyrd-tool` (from latest `main`) |
| Delivery | One pull request to `main`, CI green |
| Roadmap | v0.4 — `dotnet wyrd asyncapi` and `dotnet wyrd diagram` |

Read `AGENTS.md` first (principles, conventions, **Working in parallel**).

## Goal

A .NET tool that turns what a Wyrd application already declares into documentation:

```bash
dotnet wyrd asyncapi --contracts samples/Shop/contracts.wyrd.txt --output samples/Shop/asyncapi.yaml
dotnet wyrd diagram  --assembly samples/Shop/bin/Debug/net10.0/Shop.dll --output docs/shop-processes.md
```

## 1. `dotnet wyrd asyncapi`

Generates an **AsyncAPI 3.0.0** document (YAML) from the integration event contract snapshot, `contracts.wyrd.txt`.
Using the snapshot (not reflection) is deliberate: it is the reviewed, versioned source of truth for public contracts.

Snapshot format (one contract version per line; `#` comments and blank lines ignored; `retired ` prefix = retired):

```
payments.payment-charged v1 | Amount: decimal; Currency: string; OrderId: string
retired shipping.order-shipped v1 | OrderId: string
```

Requirements:

- One **channel** per contract name (`payments.payment-charged`), one **message** per version (`payments.payment-charged.v1`).
- Each message has a JSON Schema `payload`: every field in `properties`; fields whose type ends with `?` are optional,
  all others go in `required`.
- Type mapping: `string` → string; `int`/`long`/`short` → integer; `decimal`/`double`/`float` → number; `bool` → boolean;
  `Guid` → string/uuid; `DateOnly` → string/date; `DateTime`/`DateTimeOffset` → string/date-time; `TimeSpan` → string/duration;
  `IReadOnlyList<T>`, `List<T>`, `T[]` → array of the mapped `T`; anything else → object with a `description` naming the type.
- Retired versions are emitted with `deprecated: true`.
- Options: `--contracts` (default `./contracts.wyrd.txt`), `--output` (default stdout), `--title` (default the folder name),
  `--version` (default `0.0.0`).
- Output is deterministic (stable ordering), so it can be committed and reviewed.
- No new package dependencies: write the YAML with a small emitter of your own.

## 2. `dotnet wyrd diagram`

Loads a compiled assembly and writes a Markdown file with one Mermaid diagram per process.

- Find every `public static` property or field of type `Wyrd.Processes.Process` and call `ToMermaid()`.
- Load the assembly in its own `AssemblyLoadContext`, resolving dependencies from its folder.
- Markdown: a `#` title, then per process a `##` heading with the process name and a ```` ```mermaid ```` block.
- Options: `--assembly` (required), `--output` (default stdout).

## Where

- `src/Wyrd.Tool/` — console app packed as a .NET tool (`PackAsTool`, `ToolCommandName` = `wyrd`, `net10.0`).
  It is under `src/`, so it is packed and versioned in lockstep automatically. Set `IsAotCompatible` to false.
- `tests/Wyrd.Tool.Tests/` — xUnit tests, same conventions as the other test projects.
- You may reference `src/Wyrd` (for `Process`). Implement your own snapshot parser: the analyzer's one depends on Roslyn.

## Out of bounds (another agent is working there)

- `src/Wyrd/Runtime/**`, `src/Wyrd/Processes/**`, `src/Wyrd/Slices/**`, `src/Wyrd.Testing/**`, `tests/Wyrd.Tests/**`.
  If you need a change there, don't make it: explain why in the PR.

## Acceptance

- [ ] `dotnet build` (warnings are errors) and `dotnet test` pass; CI is green.
- [ ] Tests cover: type mapping, optional vs required, retired versions, ordering, comments/blank lines, a malformed line
      (clear error with the line number), and a diagram generated from a test assembly with at least two processes.
- [ ] Generated and committed for the sample: `samples/Shop/asyncapi.yaml` and `docs/shop-processes.md`.
- [ ] Docs: a short `dotnet wyrd` section in `README.md`; one line under *Unreleased* in `CHANGELOG.md`; tick the two
      items in `docs/ROADMAP.md` (v0.4); add `Wyrd.Tool` to the packages table in `docs/RELEASING.md`.
- [ ] PR description: what was built, how to run it, an excerpt of the generated AsyncAPI and of a diagram.
