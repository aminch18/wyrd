# Contributing to Wyrd

Thanks for considering it! Wyrd is young, so design discussions are as valuable as code.

## Before you start

- Read the [README](README.md) for the vision and the [roadmap](docs/ROADMAP.md) for what exists.
- For anything bigger than a small fix, open an issue first so we agree on the design.

## Build and test

Requires the .NET 10 SDK.

```bash
dotnet build
dotnet test
dotnet run --project samples/Shop
```

## Principles

1. **Pure core, framework-owned effects.** User code decides; Wyrd does I/O.
2. **One model, two syntaxes.** Fluent and pipe must build an identical `Process`.
3. **Deterministic.** Same inputs, same journal. No wall-clock time, no randomness in the core.
4. **Readable over clever.** The public API must be readable by a junior .NET developer.

## Pull requests

- Keep them focused; one concern per PR.
- Add tests. Runtime behaviour is tested through `InMemoryRuntime`.
- Update `CHANGELOG.md` under *Unreleased*.
- Releases are cut from tags; see [docs/RELEASING.md](docs/RELEASING.md).
