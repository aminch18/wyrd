# Design: Urd — replay the past

> Status: **proposal for v0.4**. Follows [semantics](semantics.md); when they disagree, semantics wins.

Skuld asks "what could happen?". Urd asks "what *did* happen, and does today's code still do the same?". A process
instance that misbehaved — in a test environment today, in production once durable hosts exist — is recorded, exported
as a file and replayed locally, deterministically, as a test.

## Decisions

### 1. Record what crossed the instance's boundary, not the world

A **history** is the ordered list of what an instance received and what it sent:

| Entry | Recorded |
|---|---|
| Started | the trigger event (type, payload), the process fingerprint |
| Sent | each command the process sent: step, undo or outcome, attempt, message id, type, payload |
| Replied | each reply: accepted (with the event), rejected (with the error), faulted, fenced / nothing to undo |
| Received | each awaited event (type, payload) |
| Timed out | a `.Within` that expired |
| Operator | resume or compensate |
| Ended | the final status and reason |

Every entry carries its virtual time relative to the start.

- **Gives:** a history is small, self-contained and enough to reproduce the instance exactly, because a process is a
  pure function of what it received.
- **Costs:** it doesn't capture the owners' state (stock levels, payments). Replay can't tell *why* an owner rejected
  a command — only that it did. Slices are tested on their own (Given/When/Then).

### 2. Replay isolates the process: owners answer from the recording

On replay, the process runs for real against today's code; every command it sends is answered with the **recorded
reply**, not by today's slices. Commands are matched in order: the n-th command the replay sends is compared with the
n-th recorded one.

- **Gives:** determinism without rebuilding the rest of the system; the replay tests exactly the process definition
  and its step functions.
- **Costs:** a change in an owner's decision logic is invisible to a process replay (by design).

### 3. Divergence is the finding

Replay stops at the **first divergence** and reports it with both sides:

- the process sent a different command (type or payload) than recorded, or none, or one more;
- the final status or reason differs;
- the recording no longer fits the code: the fingerprint differs (the structure changed), or a recorded payload no
  longer deserializes into today's type.

A replay without divergence passes. `ThrowIfDiverged()` turns a report into a test failure, like Skuld's
`ThrowIfViolated()`.

### 4. History is written in the same transaction as the instance

Each `Changes` appends the instance's new entries; the store commits them with the instance, the inbox and the outbox.

- **Gives:** a history never misses an effect that happened, and never contains one that didn't.
- **Costs:** storage grows with every message; retention (keep N days, keep only instances that were suspended…) comes
  with the durable hosts (v0.5).

### 5. The file format is JSON, with the registered type names

`InstanceHistory.ToJson()` / `InstanceHistory.FromJson(...)`: entries with type names as registered in the runtime and
payloads as stored. Integration events keep their contract name and version, so upcasters apply on replay.

### 6. `--as-test` writes a test, not a snapshot tool

`dotnet wyrd replay <history.json> --as-test` writes an xUnit test that embeds the history and calls
`Urd.ReplayAsync(runtime, history).ThrowIfDiverged()`. The runtime factory is the application's own
(`--runtime "ShopModule.CreateRuntime()"`). The bug becomes a regression test in one command.

## API sketch

```csharp
// Capture (in memory today, durable hosts in v0.5)
InstanceHistory history = runtime.HistoryOf(Processes.OrderFulfillment, "A-1");
File.WriteAllText("A-1.history.json", history.ToJson());

// Replay (Wyrd.Testing)
var report = await Urd.ReplayAsync(ShopModule.CreateRuntime, Processes.OrderFulfillment,
    InstanceHistory.FromJson(File.ReadAllText("A-1.history.json")));
report.ThrowIfDiverged();
```

## Out of scope for v0.4

- Reading histories from a durable store (`dotnet wyrd replay --from postgres://…`): v0.5.
- Replaying several instances together, or the owners' decisions: Skuld covers the system; slices have their own tests.
- Editing a history to explore "what if": a Skuld scenario does that.
