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

0. **Check the [quality bar](QUALITY.md)** (from 0.5.0 on): every rule ✅ and green on `main`, coverage not below the
   previous release, and the benchmark results for the release notes (orders/s and KB/order per scenario, Q1.4).
   Move each package's `PublicAPI.Unshipped.txt` entries into `PublicAPI.Shipped.txt` when releasing;
   keep the nullable header and clear Unshipped. Document any intentional breaking change under *Breaking*.
   Update each package's line and branch floors in `coverage.json` from the release's Ubuntu CI
   `coverage-report/Summary.json` artifact (`coverage` and `branchcoverage`); never lower an existing floor.
   The initial floor was measured on `main` at `b621286`. CI uses ReportGenerator to merge all test suites and
   excludes generated `*.g.cs` files; normal local `dotnet test` does not collect coverage.
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
5. After the packages appear on nuget.org, set `PackageValidationBaselineVersion` in `src/Directory.Build.props`
   to the newly published library version in the first commit of the next cycle. Keep validating the release
   against the previous version until publication completes.

## Publishing to nuget.org

Packages are pushed with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): nuget.org
trusts the release job of this repository, and each run gets a short-lived API key. No key is stored anywhere.
The job skips the push (with a notice in the log) while the repository variable `NUGET_USER` is not set.
One-time setup by the owner:

1. Sign in at [nuget.org](https://www.nuget.org/) and open *your user > Trusted Publishing > Create*:
   repository owner `aminch18`, repository `wyrd`, workflow file `ci.yml`, environment empty.
2. In GitHub, *Settings > Secrets and variables > Actions > Variables > New repository variable*: name `NUGET_USER`,
   value your nuget.org **username** (not your email).
   If Trusted Publishing can't be set up, a classic API key works too: create one scoped to `Wyrd*` with push
   rights (*your user > API Keys*) and store it as the repository **secret** `NUGET_API_KEY`.
3. Later, optional: ask for the `Wyrd` prefix reservation by email to account@nuget.org (prefix, nuget.org username,
   repository URL). Reserved packages show as verified and nobody else can publish `Wyrd.*`; nuget.org reviews
   requests by hand and expects some adoption first.
4. Push a version tag as in the steps above. Packages appear on nuget.org after validation (a few minutes).
   A published version can't be deleted, only unlisted.

## Packages

All packages are versioned in **lockstep**: one tag versions every package, so `Wyrd 0.2.0` always goes with
`Wyrd.Testing 0.2.0`. No compatibility matrix.

| Package | Contents |
|---|---|
| `Wyrd` | Messages, slices, processes, projections, the engine and its in-memory host, analyzers and code fixes |
| `Wyrd.Testing` | Given/When/Then scenarios for slices and projections; Skuld failure simulation |
| `Wyrd.Tool` | `dotnet wyrd` CLI: AsyncAPI, diagrams and Urd replay |
| `Wyrd.PostgreSql`, `Wyrd.RabbitMq`, `Wyrd.AspNetCore` *(planned)* | Verdandi: production infrastructure |
