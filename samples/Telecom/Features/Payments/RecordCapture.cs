using Wyrd;

namespace Telecom;

public sealed record RecordCapture(Offer Offer, string Receipt) : ICommand;

public sealed record PaymentCaptured(Offer Offer, string Receipt) : IDomainEvent;

public sealed class RecordCaptureSlice : Slice<RecordCapture, Payment, PaymentCaptured, RuleError>
{
    public override object KeyOf(RecordCapture command) => command.Offer.SubscriptionId;

    public override Result<PaymentCaptured, RuleError> Decide(RecordCapture command, Payment state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        return state.Capture(command.Receipt);
    }

    private static RuleError? Validate(RecordCapture command, Payment state)
    {
        if (state.Offer != command.Offer)
        {
            return RuleError.WrongOwner;
        }

        return null;
    }
}
