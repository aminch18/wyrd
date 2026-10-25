using Wyrd;

namespace Telecom;

public sealed record RecordProviderCapture(string SubscriptionId, string Receipt) : ICommand;

public sealed class RecordProviderCaptureSlice : Slice<RecordProviderCapture, Payment, PaymentCaptured, RuleError>
{
    public override object KeyOf(RecordProviderCapture command) => command.SubscriptionId;
    public override Result<PaymentCaptured, RuleError> Decide(RecordProviderCapture command, Payment state) => state.Capture(command.Receipt);
}
