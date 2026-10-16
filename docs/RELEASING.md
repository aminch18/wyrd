# Releasing

Versions follow [Semantic Versioning](https://semver.org/). The **git tag is the version**: MinVer stamps it
into every package, so there is no version number to edit in any file.

| Kind | Tag | Example |
|---|---|---|
| Preview of an unfinished milestone | `v<major>.<minor>.<patch>-alpha.<n>` | `v0.1.0-alpha.1` |
| Feature-complete, still stabilising | `v<major>.<minor>.<patch>-beta.<n>` / `-rc.<n>` | `v0.1.0-rc.1` |
| Release | `v<major>.<minor>.<patch>` | `v0.1.0` |

Before 1.0, minor versions may contain breaking changes; they are always called out in the changelog.

## Steps

1. Move the `Unreleased` entries in `CHANGELOG.md` under a new `## [x.y.z] - YYYY-MM-DD` heading and update the
   compare links at the bottom.
2. Commit: `Release x.y.z`.
3. Tag and push:

   ```bash
   git tag vx.y.z
   git push origin main vx.y.z
   ```

4. CI builds, tests, packs and creates the GitHub Release with the packages and the changelog notes
   (marked as pre-release when the tag has a suffix).

Publishing to nuget.org is prepared in `.github/workflows/ci.yml` and will be enabled when the repository goes public.

## Packages

All packages are versioned in **lockstep**: one tag versions every package, so `Wyrd 0.2.0` always goes with
`Wyrd.Testing 0.2.0`. No compatibility matrix.

| Package | Contents |
|---|---|
| `Wyrd` | Messages, slices, processes, projections, the engine and its in-memory host, analyzers and code fixes |
| `Wyrd.Testing` | Given/When/Then scenarios for slices and projections; Skuld simulation *(planned)* |
| `Wyrd.Tool` *(planned)* | `dotnet wyrd` CLI: Urd replay, diagrams, AsyncAPI |
| `Wyrd.PostgreSql`, `Wyrd.RabbitMq`, `Wyrd.AspNetCore` *(planned)* | Verdandi: production infrastructure |
