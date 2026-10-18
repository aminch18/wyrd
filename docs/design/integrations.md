# Design: Integrations — the only I/O

> Status: **v0.5**. Follows [semantics](semantics.md).

Slices decide; they never call the outside world. But a payment has to reach a payment provider, an email has to be
sent. An **integration** is the one place where Wyrd code performs I/O, and it is shaped so the guarantees survive it.

## Shape

```csharp
public sealed class PaymentGateway(HttpClient http) : Integration<ChargeCard, CardCharged, PaymentError>
{
    public override async Task<Result<CardCharged, PaymentError>> ExecuteAsync(
        ChargeCard command, IntegrationContext context, CancellationToken cancellationToken)
    {
        // context.IdempotencyKey is the same on every retry and redelivery: the provider deduplicates with it.
        var response = await http.PostAsJsonAsync("/charges", new { command.OrderId, command.Amount,
            idempotencyKey = context.IdempotencyKey }, cancellationToken);
        return response.StatusCode == HttpStatusCode.PaymentRequired
            ? PaymentError.Declined                                     // a business answer: never retried
            : new CardCharged(command.OrderId, await response.Content.ReadAsStringAsync(cancellationToken));
    }
}

runtime.AddIntegration(new PaymentGateway(http));
```

## Decisions

1. **An integration owns a command, like a slice.** One owner per command, slice or integration; a process step
   doesn't know which one answers. *Gives:* a process can move from a fake to a real provider without changing.
   *Costs:* an integration has no state of its own; what it needs to remember is an event.
2. **Three outcomes, the same as a slice.** `Ok(event)` → accepted (the event is stored and published like a domain
   event); `Error` → rejected (a business answer, never retried); **an exception → a transient failure**, and the
   process's retry policy decides. *Gives:* one model for processes. *Costs:* an integration must translate a
   provider's business errors into `Error` and let infrastructure errors throw.
3. **The idempotency key is the command's message id.** Retries and redeliveries reuse it, so a provider that
   supports idempotency keys (most payment APIs do) never charges twice. *Costs:* a provider without them can, in rare
   crashes, see a call twice — the integration must say so in its documentation.
4. **The outcome is recorded.** The reply is stored in the owner's inbox with the event: a duplicate delivery after
   the outcome was committed is answered from the inbox without calling the provider again; Urd replays it without
   I/O; fencing works as for slices.
5. **Timeouts are the integration's own business.** Wyrd adds no technical timeout ([semantics](semantics.md) 3.5);
   an `HttpClient` timeout is the integration author's choice and surfaces as a transient failure.

## Not in scope

Webhooks are not integrations: a provider calling back is a fact from outside (`PublishAsync` of an integration
event), as today.
