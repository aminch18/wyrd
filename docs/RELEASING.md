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

## Publishing to nuget.org

The release job pushes every `.nupkg` and `.snupkg` with `--skip-duplicate` when the repository secret
`NUGET_API_KEY` exists, and skips the step (with a notice in the log) when it does not. One-time setup by the owner:

1. Sign in at [nuget.org](https://www.nuget.org/) (create an account if needed) and reserve the `Wyrd` prefix:
   *Account > Manage Prefixes* (`Wyrd*`). Prefix reservation is optional but shows the packages as verified.
2. Create an API key: *Account > API Keys > Create*. Key name `wyrd-github-actions`, scope **Push new packages and
   package versions**, glob pattern `Wyrd*`, and a limited expiry (renew it when it lapses).
3. In GitHub, open *Settings > Secrets and variables > Actions > New repository secret* on `aminch18/wyrd`.
   Name it `NUGET_API_KEY` and paste the key.
4. Push a version tag as in the steps above. Packages appear on nuget.org after validation (a few minutes).

Pushing is irreversible: a published version can only be unlisted, never replaced. Check the GitHub Release
assets first, and keep previews on `-alpha.n` tags.

## Packages

All packages are versioned in **lockstep**: one tag versions every package, so `Wyrd 0.2.0` always goes with
`Wyrd.Testing 0.2.0`. No compatibility matrix.

| Package | Contents |
|---|---|
| `Wyrd` | Messages, slices, processes, projections, the engine and its in-memory host, analyzers and code fixes |
| `Wyrd.Testing` | Given/When/Then scenarios for slices and projections; Skuld failure simulation |
| `Wyrd.Tool` | `dotnet wyrd` CLI: AsyncAPI and diagrams; Urd replay *(planned)* |
| `Wyrd.PostgreSql`, `Wyrd.RabbitMq`, `Wyrd.AspNetCore` *(planned)* | Verdandi: production infrastructure |
