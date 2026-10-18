# Demo script

A reproducible five-minute walkthrough. Everything runs from a fresh clone with the .NET 10 SDK; no database,
broker or Docker. Outputs below are excerpts from a real run (journals are deterministic, but Skuld's counts can
change when the engine or the sample change).

```bash
git clone https://github.com/aminch18/wyrd.git && cd wyrd
dotnet build
```

Each experiment edits a sample file and says how to undo it.

## 1. A breaking contract change fails the build

`payments.payment-charged` v1 is a public contract, snapshotted in `samples/Shop/contracts.wyrd.txt`:

```text
payments.payment-charged v1 | Amount: decimal; Currency: string; OrderId: string
```

Change the type of `Amount` in `samples/Shop/Contracts/IntegrationEvents.cs`:

```bash
sed -i.bak 's/decimal Amount, string Currency/double Amount, string Currency/' samples/Shop/Contracts/IntegrationEvents.cs
dotnet build samples/Shop
```

```text
IntegrationEvents.cs(12,22): error WYRD0201: Breaking change in integration event 'payments.payment-charged' v1:
field 'Amount' changed type from decimal to double. Existing subscribers would fail to read it. Publish it as v2
and keep v1 until subscribers migrate, or revert the change.
```

The compiler also complains in `Payments/Publications.cs`, because the publication still maps a `decimal`.
That error disappears if you update the mapping, and the build stays red: only the snapshot check stops you from
shipping a contract that existing subscribers can no longer read. Undo with:

```bash
mv samples/Shop/Contracts/IntegrationEvents.cs.bak samples/Shop/Contracts/IntegrationEvents.cs
```

## 2. Skuld finds the bug in a fragile process

First, the healthy shop. Skuld explores 200 futures, then 5,000 hostile ones (20% transient faults, 20% duplicates,
50% reordering):

```bash
dotnet test samples/Shop.Tests --filter SkuldSpecs
```

```text
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2
```

Now make the process fragile: in `samples/Shop/Fulfillment/OrderFulfillment.cs`, delete every `.Retry(...)`
(`.Retry(5.Times)` and `.Retry(5.Times, Backoff.Exponential)`), so the process reads:

```csharp
public static Process OrderFulfillment => Process
    .StartOn<OrderPlaced>(e => e.OrderId)
    .Then(Stock.Reserve).Undo(Stock.Release)
    .Then(Payments.Charge).Undo(Payments.Refund)
    .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
    .Then(Shipping.Ship)
    .OnCompensated(Orders.Cancel);
```

It compiles, and the happy path still passes. Run the specs again:

```bash
dotnet test samples/Shop.Tests --filter the_business_holds_in_every_future
```

```text
Skuld explored 200 futures: 83 broke an invariant.

✗ "stock is conserved" broke in 48 futures. First: seed 5.
  3 keyboards available, expected 5 received - 1 shipped
  Perturbations: delivered reply to OrderFulfillment A-1 before PaymentCharged; ...; will deliver ReserveStock (A-2) twice; ...
  Journal:
    [00:00:00] -                KEYBOARD -> Restock
    ...
  Replay it: Skuld.SimulateAsync(..., options.Only(5))
```

The report lists each broken invariant, the first seed that breaks it, the perturbations applied and the journal.
Replaying the seed reproduces the future exactly. Two more invariants break in the same run:
`nothing needs attention` (a step or compensation that gave up after one transient failure) and
`every order ends shipped or cancelled`. Put the retries back with:

```bash
git checkout samples/Shop/Fulfillment/OrderFulfillment.cs
```

## 3. The Shop API

```bash
dotnet run --project samples/Shop.Api      # http://localhost:5080
```

In another terminal (or use `samples/Shop.Api/Shop.Api.http` from your IDE):

```bash
curl -i -X POST localhost:5080/stock/KEYBOARD -H 'content-type: application/json' -d '{"quantity":5}'
curl -i -X POST localhost:5080/orders -H 'content-type: application/json' \
  -d '{"orderId":"A-1001","sku":"KEYBOARD","quantity":1,"amount":89}'
curl localhost:5080/orders
```

```text
HTTP/1.1 202 Accepted
[{"orderId":"A-1001","sku":"KEYBOARD","quantity":1,"amount":89,"status":"Paid"}]
```

The payment provider confirms, the process ships the order, and a second identical order is rejected:

```bash
curl -i -X POST localhost:5080/webhooks/payments -H 'content-type: application/json' -d '{"orderId":"A-1001"}'
curl localhost:5080/orders/A-1001
curl -i -X POST localhost:5080/orders -H 'content-type: application/json' \
  -d '{"orderId":"A-1001","sku":"KEYBOARD","quantity":1,"amount":89}'
```

```text
HTTP/1.1 202 Accepted
{"order":{"orderId":"A-1001",...,"status":"Shipped"},"fulfillment":"Completed"}
HTTP/1.1 409 Conflict
{"title":"AlreadyPlaced","status":409,...}
```

The API runs on the in-memory runtime, so its data is lost on restart. The console version of the same shop
(`dotnet run --project samples/Shop.Console`) prints the full journal for six scenarios, including the abandoned
3-D Secure timeout and its compensation.

## Recording a terminal GIF

[`demo.tape`](demo.tape) is a [vhs](https://github.com/charmbracelet/vhs) script covering experiments 1 and 2.
`vhs` was not installed when this was written, so the GIF has not been recorded yet.

```bash
brew install vhs            # also installs ttyd and ffmpeg
vhs docs/launch/demo.tape   # writes docs/launch/demo.gif
```

Run it from the repository root on a clean working tree; the tape restores the files it edits.
