using Wyrd;

namespace Telecom;

public sealed record ReconcilePayment(Offer Offer, int Review) : ICommand;

public sealed record PaymentReconciliationRequested(Offer Offer, int Review) : IDomainEvent;

public sealed class ReconcilePaymentSlice : Slice<ReconcilePayment, Payment, PaymentReconciliationRequested, RuleError>
{
    public override object KeyOf(ReconcilePayment command) => command.Offer.SubscriptionId;

    public override Result<PaymentReconciliationRequested, RuleError> Decide(ReconcilePayment command, Payment state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        return new PaymentReconciliationRequested(command.Offer, command.Review);
    }

    private static RuleError? Validate(ReconcilePayment command, Payment state)
    {
        if (state.Offer != command.Offer)
        {
            return RuleError.WrongOwner;
        }

        if (command.Review <= state.Review)
        {
            return RuleError.StaleAttempt;
        }

        return null;
    }
}
