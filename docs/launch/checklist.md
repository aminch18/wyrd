# Preview launch checklist

## Done

- [x] Repository is public; description, topics and Discussions are set.
- [x] Dependabot updates merged (GitHub Actions and test packages); stale merged branches deleted.
- [x] CI runs build and test on Linux, Windows and macOS; Native AOT smoke test and pack stay on Linux.
- [x] Release job pushes `.nupkg` and `.snupkg` to nuget.org with Trusted Publishing when `NUGET_USER` is set, and skips otherwise.
- [x] README opens with what Wyrd is, an honest preview status and a two-minute way to try it.
- [x] `CODE_OF_CONDUCT.md`, issue templates (Discussions link) and `SECURITY.md` in place.
- [x] Demo script and terminal recording script: [demo.md](demo.md), [demo.tape](demo.tape).
- [x] Post drafts: [posts.md](posts.md).

## Owner steps

1. **NuGet Trusted Publishing (once).** Follow "Publishing to nuget.org" in [../RELEASING.md](../RELEASING.md): create
   the trusted publishing policy on nuget.org and set the repository variable `NUGET_USER`. No API key needed.
2. **Decide on the Verdandi status line.** The README says the durable runtime is in progress. If that changes before
   the tag, update the README warning and the `CHANGELOG.md` entry first.
3. **Merge the launch PR**, then update `main` locally.
4. **Prepare the release.** Move *Unreleased* in `CHANGELOG.md` under `## [0.3.0] - <date>`, update the compare
   links, commit `Release 0.3.0`, and let CI go green on `main`.
5. **Tag.** `git tag v0.3.0 && git push origin main v0.3.0`. CI creates the GitHub pre-release and,
   with the secret present, pushes the packages. Check the three packages (`Wyrd`, `Wyrd.Testing`, `Wyrd.Tool`) on nuget.org.
   A published version cannot be replaced, only unlisted.
6. **Record the GIF (optional).** `brew install vhs`, then `vhs docs/launch/demo.tape`; add the GIF to the README
   if it looks good.
7. **Seed Discussions.** Post a welcome thread and a "feedback wanted" thread; pin both.
8. **Post**, in this order, after the packages are visible: LinkedIn (EN, then ES), r/dotnet, X thread
   ([posts.md](posts.md)). Be around for the first hours to answer comments.
9. **Triage feedback.** Label early issues (`good first issue`, `question`) and keep the roadmap honest.
