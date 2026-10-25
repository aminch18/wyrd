using Wyrd;
using Wyrd.Processes;

namespace Telecom;

public static class Flows
{
    public static Process Contracting => Process.StartOn<SubscriptionOpened>(e => e.Offer.SubscriptionId)
        .Then(Claim).Retry(3.Times).Undo(Release)
        .Then(Admit).Retry(3.Times)
        .OnCompensated(Decline).Retry(3.Times);

    public static Process PaymentOpening => Process.StartOn<SubscriptionAdmitted>(e => e.Offer.SubscriptionId)
        .Then(OpenPayment).Retry(3.Times);

    public static Process PaymentCollection => Process.StartOn<PaymentOpened>(e => e.Offer.SubscriptionId)
        .Then(Capture).Retry(3.Times).NoCompensation("A possibly captured payment needs reconciliation, never a fictional void")
        .Then(StoreCapture).Retry(3.Times)
        .OnCompensated(PaymentIncident).Retry(3.Times);

    public static Process PaymentConfirmation => Process.StartOn<PaymentConfirmedV1>(e => e.SubscriptionId)
        .Then(ConfirmPayment).Retry(3.Times);

    public static Process PaymentReconciliation => Process.StartOn<PaymentReconciliationRequested>(e => $"{e.Offer.SubscriptionId}/{e.Review}")
        .Then(Find).Retry(3.Times).NoCompensation("Read-only provider inquiry")
        .Then(StoreReconciledCapture).Retry(3.Times)
        .OnCompensated(ReconciliationIncident).Retry(3.Times);

    public static Process PortSubmission => Process.StartOn<PaymentCaptured>(e => PortKey(e.Offer.SubscriptionId, e.Offer.Msisdn, e.Offer.Epoch))
        .Then(Submit).Retry(3.Times).NoCompensation("Request cancellation is a separate protocol; completion cannot be reversed")
        .Then(StoreSubmission).Retry(3.Times).NoCompensation("Submission is a fact")
        .Await<PortCompletedV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch)).Within(2.Days)
        .OnCompensated(PortIncident).Retry(3.Times);

    public static Process PortConfirmation => Process.StartOn<PortConfirmedV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch))
        .Then(ConfirmPort).Retry(3.Times);
    public static Process PortCompletion => Process.StartOn<PortCompletedV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch))
        .Then(CompletePort).Retry(3.Times);
    public static Process PortCancellation => Process.StartOn<PortCancellationRequested>(e => $"{e.Offer.SubscriptionId}/{e.Offer.Epoch}")
        .Then(CancelPort).Retry(3.Times);
    public static Process CancellationConfirmation => Process.StartOn<PortCancelledV1>(e => PortKey(e.SubscriptionId, e.Msisdn, e.Epoch))
        .Then(ConfirmCancellation).Retry(3.Times);

    public static Process InitialProfile => Process.StartOn<PaymentCaptured>(e => e.Offer.SubscriptionId)
        .Then(Assign).Retry(3.Times);
    public static Process ProfileProvisioning => Process.StartOn<ProfileAssigned>(e => ProfileKey(e.SubscriptionId, e.ProfileId, e.Attempt))
        .Then(ClaimProfile).Retry(3.Times).NoCompensation("Keep the lease quarantined until external deactivation is verified")
        .Then(Provision).Retry(3.Times).NoCompensation("Provisioning may have succeeded even if its response was lost")
        .Then(StoreProvision).Retry(3.Times).NoCompensation("Provisioned is a fact, not verified connectivity")
        .Await<ConnectivityVerifiedV1>(e => ProfileKey(e.SubscriptionId, e.ProfileId, e.Attempt)).Within(30.Minutes)
        .Then(StoreConnectivity).Retry(3.Times)
        .OnCompensated(ActivationIncident).Retry(3.Times);

    public static Process PaymentEvidence => Process.StartOn<PaymentCaptured>(e => e.Offer.SubscriptionId)
        .Then(ObservePayment).Retry(3.Times);
    public static Process PortEvidence => Process.StartOn<NumberChanged>(e => $"{e.Offer.SubscriptionId}/{e.Phase}")
        .Then(ObservePort).Retry(3.Times);
    public static Process ActivationAssessment => Process.StartOn<ActivationObserved>(e => e.EvidenceKey)
        .Then(Assess).Retry(3.Times);
    public static Process BillingCommencement => Process.StartOn<ServiceOperationalV1>(e => e.SubscriptionId)
        .Then(Bill).Retry(3.Times);

    public static string PortKey(string id, string msisdn, long epoch) => $"{id}/{msisdn}/{epoch}";
    public static string ProfileKey(string id, string profile, int attempt) => $"{id}/{profile}/{attempt}";
    private static ClaimNumber Claim(SubscriptionOpened e) => new(e.Offer);
    private static ReleaseUnsubmittedNumber Release(SubscriptionOpened e) => new(e.Offer);
    private static AdmitSubscription Admit(SubscriptionOpened e) => new(e.Offer);
    private static DeclineSubscription Decline(SubscriptionOpened e) => new(e.Offer.SubscriptionId);
    private static OpenPayment OpenPayment(SubscriptionAdmitted e) => new(e.Offer);
    private static CapturePayment Capture(PaymentOpened e) => new(e.Offer);
    private static RecordCapture StoreCapture(PaymentOpened e, CaptureReceived receipt) => new(e.Offer, receipt.Receipt);
    private static RecordProviderCapture ConfirmPayment(PaymentConfirmedV1 e) => new(e.SubscriptionId, e.Receipt);
    private static FindPayment Find(PaymentReconciliationRequested e) => new(e.Offer);
    private static RecordCapture StoreReconciledCapture(PaymentReconciliationRequested e, CaptureReceived receipt) => new(e.Offer, receipt.Receipt);
    private static SubmitPort Submit(PaymentCaptured e) => new(e.Offer);
    private static MarkPortSubmitted StoreSubmission(PaymentCaptured e) => new(e.Offer);
    private static ApplyPortNotice ConfirmPort(PortConfirmedV1 e) => new(e.SubscriptionId, e.Msisdn, e.Epoch, PortFact.Confirmed, e.WindowStart, e.WindowEnd);
    private static ApplyPortNotice CompletePort(PortCompletedV1 e) => new(e.SubscriptionId, e.Msisdn, e.Epoch, PortFact.Completed, default, default);
    private static ApplyPortNotice ConfirmCancellation(PortCancelledV1 e) => new(e.SubscriptionId, e.Msisdn, e.Epoch, PortFact.Cancelled, default, default);
    private static CancelPort CancelPort(PortCancellationRequested e) => new(e.Offer);
    private static AssignProfile Assign(PaymentCaptured e) => new(e.Offer.SubscriptionId, e.Offer.CustomerId, e.Offer.ProfileId, 1);
    private static ClaimProfile ClaimProfile(ProfileAssigned e) => new(e.SubscriptionId, e.CustomerId, e.ProfileId, e.Attempt);
    private static ProvisionProfile Provision(ProfileAssigned e) => new(e.SubscriptionId, e.CustomerId, e.ProfileId, e.Attempt);
    private static ObserveActivation StoreProvision(ProfileAssigned e) => new(e.SubscriptionId, ActivationFact.Provisioned, e.ProfileId, e.Attempt);
    private static ObserveActivation StoreConnectivity(ProfileAssigned e, ConnectivityVerifiedV1 verified) => new(e.SubscriptionId, ActivationFact.Connectivity, verified.ProfileId, verified.Attempt);
    private static ObserveActivation ObservePayment(PaymentCaptured e) => new(e.Offer.SubscriptionId, ActivationFact.Payment);
    private static RecordCompletedPort ObservePort(NumberChanged e) => new(e.Offer.SubscriptionId, e.Phase);
    private static AssessActivation Assess(ActivationObserved e) => new(e.SubscriptionId);
    private static BeginBilling Bill(ServiceOperationalV1 e) => new(e.SubscriptionId, e.ProfileId, e.Attempt);
    private static OpenIncident PaymentIncident(PaymentOpened e) => new(e.Offer.SubscriptionId, "payment", 0);
    private static OpenIncident ReconciliationIncident(PaymentReconciliationRequested e) => new(e.Offer.SubscriptionId, "reconciliation", e.Review);
    private static OpenIncident PortIncident(PaymentCaptured e) => new(e.Offer.SubscriptionId, "port-deadline", 0);
    private static OpenIncident ActivationIncident(ProfileAssigned e) => new(e.SubscriptionId, "connectivity", e.Attempt);
}

// A linear process cannot filter a trigger. The domain command rejects non-completion facts;
// its one-step process compensates with no undo. This extra plumbing is documented as a DSL cost.
public sealed record RecordCompletedPort(string SubscriptionId, PortPhase Phase) : ICommand;
public sealed class RecordCompletedPortSlice : Slice<RecordCompletedPort, Activation, ActivationObserved, RuleError>
{
    public override object KeyOf(RecordCompletedPort command) => command.SubscriptionId;
    public override Result<ActivationObserved, RuleError> Decide(RecordCompletedPort command, Activation state) => command.Phase != PortPhase.Completed
        ? RuleError.NotReady : new ActivationObserved(command.SubscriptionId, $"{command.SubscriptionId}/port", state with { Ported = true });
}
