using Wyrd;

namespace Telecom;

public sealed record OpenPayment(Offer Offer) : ICommand;

public sealed record PaymentOpened(Offer Offer) : IDomainEvent;

public sealed class OpenPaymentSlice : Slice<OpenPayment, Payment, PaymentOpened, RuleError>
{
    public override object KeyOf(OpenPayment command) => command.Offer.SubscriptionId;

    public override Result<PaymentOpened, RuleError> Decide(OpenPayment command, Payment state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        return new PaymentOpened(command.Offer);
    }

    private static RuleError? Validate(OpenPayment command, Payment state)
    {
        if (!command.Offer.IsValid)
        {
            return RuleError.InvalidInput;
        }

        if (state.Phase != PaymentPhase.Empty)
        {
            return RuleError.AlreadyExists;
        }

        return null;
    }
}
