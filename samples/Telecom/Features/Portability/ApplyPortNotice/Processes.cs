using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process PortConfirmation => Process.StartOn<PortConfirmedV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch))
        .Then(ConfirmPort).Retry(3.Times);

    public static Process PortCompletion => Process.StartOn<PortCompletedV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch))
        .Then(CompletePort).Retry(3.Times);

    public static Process CancellationConfirmation => Process.StartOn<PortCancelledV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch))
        .Then(ConfirmCancellation).Retry(3.Times);

    private static ApplyPortNotice ConfirmPort(PortConfirmedV1 confirmed) => new(confirmed.SubscriptionId, confirmed.Msisdn, confirmed.Epoch, PortFact.Confirmed, confirmed.WindowStart, confirmed.WindowEnd);

    private static ApplyPortNotice CompletePort(PortCompletedV1 completed) => new(completed.SubscriptionId, completed.Msisdn, completed.Epoch, PortFact.Completed, default, default);

    private static ApplyPortNotice ConfirmCancellation(PortCancelledV1 cancelled) => new(cancelled.SubscriptionId, cancelled.Msisdn, cancelled.Epoch, PortFact.Cancelled, default, default);
}
