# Task 006 — Diagrams that show the decisions

| | |
|---|---|
| Branch | `codex/richer-diagrams` |

Read first: `AGENTS.md`, `docs/design/semantics.md` section 3, `src/Wyrd/Processes/Process.cs` (`ToMermaid`),
`src/Wyrd/Processes/ProcessNode.cs`, `tests/Wyrd.Tests/MermaidTests.cs` (or wherever `ToMermaid` is tested),
`src/Wyrd.Tool/Diagrams.cs`.

## Goal

`ToMermaid()` is living documentation, but it hides most of what a reviewer must check. Show:

- per step: `.Retry(n, backoff)`, `.NoRetry("why")` (the reason), `.OrCompensate()`;
- per undo: how it insists when adjusted (`.Undo(x, 20.Times)`), `.NoCompensation("why")`;
- the events a step reads (`StepRef.Reads`), e.g. a dotted edge from the step that produces them;
- waits with `.Within(...)`, and the outcome (`OnCompensated`) with its policy.

Keep the diagram readable on GitHub (Mermaid `flowchart TD`); prefer short labels plus a legend over long text.

## Bounds

- In: `ToMermaid` in `src/Wyrd/Processes/Process.cs` (or a new `src/Wyrd/Processes/ProcessDiagram.cs` it calls), its
  tests, regenerated diagrams under `docs/` if any are checked in, your lines in `CHANGELOG.md`.
- Out: everything else in `src/Wyrd/**` (the runtime is being changed by another agent), `src/Wyrd.Tool/**`.

## Acceptance

- [ ] Snapshot-style tests for a process using every modifier, in fluent and pipe syntax (same output).
- [ ] The Shop's diagram renders on GitHub (paste the output in the PR description).
