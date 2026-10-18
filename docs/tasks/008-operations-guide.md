# Task 008 — Guide: operating Wyrd (suspended instances)

| | |
|---|---|
| Branch | `codex/operations-guide` |

Read first: `AGENTS.md`, `docs/design/semantics.md` 3.1, 3.4–3.7, `tests/Wyrd.Tests/SuspensionTests.cs`,
`FingerprintTests.cs`, `samples/Shop.Api/Program.cs` (dev endpoints), `docs/guides/read-models.md` (tone).

## Goal

`docs/guides/operations.md` for the person on call: what Wyrd does when it can't resolve something and what they do.

- Why Wyrd suspends instead of guessing; every reason (`ReasonOf`) with an example message.
- Decision tree: resume (after a fix; same message id, so nothing runs twice) vs compensate (fences the step it gave
  up on) vs wait (a late success resumes it on its own); `.OrCompensate()` for steps where automatic compensation is
  the business's choice.
- A redeploy that changes a process's structure: what happens, how to roll back and resume; migration is v1.0.
- Undos that keep failing; outcome commands that keep failing.
- Honest limits: in-memory host only today; durable hosts and observability (age of pending steps, alerts) in v0.5.
- Add `.http` examples for the Shop's resume/compensate dev endpoints in `samples/Shop.Api/Shop.Api.http`.

## Bounds

- In: `docs/guides/operations.md` (new), `samples/Shop.Api/Shop.Api.http`, links from `README.md` (documentation
  list, absolute URLs) and `docs/guides/read-models.md` ("continue with").
- Out: all code. Never describe a planned feature as available.

## Acceptance

- [ ] Every API and message quoted exists (copy journal lines from a real run).
