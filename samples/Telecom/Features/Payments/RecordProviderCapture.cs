using Wyrd.Processes;
using Wyrd;

namespace Telecom;

public sealed record RecordProviderCapture(string SubscriptionId, string Receipt) : ICommand;

public sealed class RecordProviderCaptureSlice : Slice<RecordProviderCapture, Payment, PaymentCaptured, RuleError>
{
    public override object KeyOf(RecordProviderCapture command) => command.SubscriptionId;
    public override Result<PaymentCaptured, RuleError> Decide(RecordProviderCapture command, Payment state) => state.Capture(command.Receipt);
}

public static partial class Flows
{
    public static Process PaymentConfirmation => Process.StartOn<PaymentConfirmedV1>(e => e.SubscriptionId)
        .Then(ConfirmPayment).Retry(3.Times);

    private static RecordProviderCapture ConfirmPayment(PaymentConfirmedV1 e) => new(e.SubscriptionId, e.Receipt);
}
