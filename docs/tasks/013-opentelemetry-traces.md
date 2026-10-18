# Task 013 — OpenTelemetry: process-level traces

| | |
|---|---|
| Branch | `codex/opentelemetry` |
| Roadmap | v0.5 — "OpenTelemetry with process-level traces" |
| Starts after | v0.4 Urd is merged into `main` (it changes `src/Wyrd/Runtime/**`) |

Read first: `AGENTS.md` (review checklist, determinism), `docs/design/semantics.md`, `src/Wyrd/Runtime/Engine.cs`,
`InMemoryRuntime.cs`, `Messaging.cs`, the .NET guide to
[distributed tracing](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs).

## Goal

An operator opening a trace sees **one process instance as one story**: the trigger, every step command, its reply
(accepted, rejected, faulted, retried), waits, timeouts, compensation and the outcome — across hosts once they exist.

1. Instrument with `System.Diagnostics.ActivitySource` (`"Wyrd"`), part of the BCL: `src/Wyrd` stays
   dependency-free and users enable it with `.AddSource("Wyrd")`. No OpenTelemetry package in the library.
2. One activity per handled envelope (kind, message type, key, attempt, outcome). The trace context travels in the
   envelope, so the activities of one instance form one trace; the instance's process name, key and status are tags.
3. **Determinism is untouched**: trace ids never feed message ids, the journal, stored state or ordering. A test
   proves the journal of the Shop scenario is identical with and without a listener; Skuld stays green.
4. Semantic conventions for messaging where they fit (`messaging.operation`, `messaging.message.id`); Wyrd-specific
   tags under `wyrd.*`, listed in a short design note in the PR (Claude moves it into `docs/design` on review).
5. `samples/Shop.Api` exports traces to the console exporter when `OTEL_EXPORTER=console` is set; README section
   (your own) with a real trace of the Shop's happy path and a compensated order.

## Bounds

- In: `src/Wyrd/Runtime/**` (only what tracing needs), `tests/Wyrd.Tests/**`, `samples/Shop.Api/Program.cs` (the
  exporter lines) and its csproj, `Directory.Packages.props` (sample packages only), your README section, your lines
  in `CHANGELOG.md`, the roadmap item.
- Out: public API changes beyond the source name, `src/Wyrd.Testing/**`, `docs/design/**`.

## Acceptance

- [ ] `dotnet build`, `dotnet test` (including `SkuldSpecs`) and the Native AOT console green.
- [ ] The PR shows a real exported trace and the determinism test.
