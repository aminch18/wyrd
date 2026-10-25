using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static partial class Flows
{
    public static Process ActivationAssessment => Process.StartOn<ActivationObserved>(e => e.EvidenceKey)
        .Then(Assess).Retry(3.Times);

    private static AssessActivation Assess(ActivationObserved observed) => new(observed.SubscriptionId);
}
