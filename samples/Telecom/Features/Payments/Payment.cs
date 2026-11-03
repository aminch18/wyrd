using Wyrd;

namespace Telecom;

public enum PaymentPhase
{
    Empty,
    Pending,
    Captured,
}

public sealed record Payment(Offer? Offer, PaymentPhase Phase, string? Receipt, int Review) : IState<Payment>
{
    public static Payment Initial { get; } = new(null, PaymentPhase.Empty, null, 0);

    // Both process replies and provider callbacks use the same receipt policy.
    public Result<PaymentCaptured, RuleError> Capture(string receipt)
    {
        var error = ValidateCapture(receipt);
        if (error is not null)
        {
            return error.Value;
        }

        return new PaymentCaptured(Offer!, receipt);
    }

    private RuleError? ValidateCapture(string receipt)
    {
        if (Offer is null)
        {
            return RuleError.WrongOwner;
        }

        if (string.IsNullOrWhiteSpace(receipt))
        {
            return RuleError.InvalidInput;
        }

        if (Receipt is not null && Receipt != receipt)
        {
            return RuleError.ConflictingReceipt;
        }

        return null;
    }

    public Payment Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        PaymentOpened e => this with { Offer = e.Offer, Phase = PaymentPhase.Pending },
        PaymentCaptured e => this with { Phase = PaymentPhase.Captured, Receipt = e.Receipt },
        PaymentReconciliationRequested e => this with { Review = e.Review },
        _ => this,
    };
}
