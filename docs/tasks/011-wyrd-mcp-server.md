# Task 011 — `dotnet wyrd mcp`: the tool as an MCP server for agents

| | |
|---|---|
| Branch | `codex/wyrd-mcp` |
| Roadmap | v1.0 — "`dotnet wyrd` CLI + MCP server for agents" (the MCP half; the CLI exists) |
| Starts after | v0.4 Urd is merged (#20): the server exposes `replay` too |

Read first: `AGENTS.md` (review checklist), `src/Wyrd.Tool/**`, `tests/Wyrd.Tool.Tests/**`,
`docs/tasks/001-wyrd-tool.md`, the [MCP specification](https://modelcontextprotocol.io/specification) and the
official C# SDK (`ModelContextProtocol` on NuGet).

## Goal

Agents that work on a Wyrd codebase should ask the tool, not guess. `dotnet wyrd mcp` starts an MCP server over
stdio that exposes what the CLI already knows:

- `diagram` — Mermaid for the public static processes in an assembly (same output as `dotnet wyrd diagram`).
- `asyncapi` — AsyncAPI 3.0 YAML from a contract snapshot (same output as `dotnet wyrd asyncapi`).
- `contracts` — the integration events in a snapshot (name, version, fields), as structured content.

Rules:

1. **One implementation.** Each MCP tool calls the same functions as its CLI command (`Diagrams`, `AsyncApi`,
   `Contracts`); if a function needs reshaping to be shared, reshape it, don't copy it.
2. Tool descriptions say what, when to use it and what each argument is, written for an agent that has never seen
   Wyrd. Errors come back as MCP tool errors with the same message the CLI prints, never as a crashed server.
3. Use the official SDK; no hand-rolled JSON-RPC. Add the package to `Directory.Packages.props`.
4. A short section in the tool's README part (`README.md`, your own section) showing how to register the server in
   an MCP client (`"command": "dotnet", "args": ["wyrd", "mcp"]`), with output copied from a real run.
5. `replay` is exposed too (timeline, and the test with `--as-test`), sharing `ReplayTest` with the CLI.

## Bounds

- In: `src/Wyrd.Tool/Mcp/**`, `src/Wyrd.Tool/Commands/McpCommand.cs`, one `AddCommand` registration in
  `src/Wyrd.Tool/Program.cs`, the shared functions in `src/Wyrd.Tool/*.cs` only as far as rule 1 needs,
  `tests/Wyrd.Tool.Tests/**`, `Directory.Packages.props` (one line), your section of `README.md`, your lines in
  `CHANGELOG.md`.
- Out: `src/Wyrd/**`, `src/Wyrd.Tool/Commands/Replay*` (use `ReplayTest`, don't change the command). Tick the
  roadmap item: with `replay` in the CLI and the MCP server, it is done.

## Acceptance

- [ ] `dotnet build` and `dotnet test` green; tests start the server in-process and call each tool through an MCP
      client, comparing with the CLI output for the Shop sample.
- [ ] The PR shows a real session: an MCP client listing the tools and calling `diagram` on `samples/Shop`.
