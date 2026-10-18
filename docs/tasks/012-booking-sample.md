# Task 012 — A second sample: hotel booking

| | |
|---|---|
| Branch | `codex/booking-sample` |

Read first: `AGENTS.md`, `docs/design/semantics.md`, `samples/Shop/**` (structure to mirror), `samples/Shop.Tests/**`.

## Goal

The Shop shows the basics. A second, small domain shows the 0.3 features where they matter most:

- `samples/Booking` (library) + `samples/Booking.Tests`: reserve a room, hold a payment, wait for the guest to confirm
  `.Within(30.Minutes)`, then confirm with the hotel. Use:
  - a step that reads earlier events (confirming with the hotel needs the hold id from the payment step);
  - `.OrCompensate()` where automatic compensation is the business's choice, and suspension where it isn't;
  - every step declaring `.Retry(...)` or `.NoRetry("why")` with a real reason;
  - a read model for the front desk; contracts snapshot for one integration event.
- Business specifications and Skuld specs with domain invariants ("a room is never double-booked", "a released hold
  is never charged").
- A short README in `samples/Booking` explaining what each feature demonstrates.

## Bounds

- In: `samples/Booking/**`, `samples/Booking.Tests/**` (new), the solution file, one line in `README.md` ("Try the
  samples"), CI only if a new smoke step is needed (run `actionlint`).
- Out: `src/**`, `samples/Shop*/**`.

## Acceptance

- [ ] `dotnet test` green, analyzers clean (no suppressions), source-generated JSON context complete.
