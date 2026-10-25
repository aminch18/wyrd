using Wyrd;

namespace Telecom;

public enum PaymentPhase { Empty, Pending, Captured }
public sealed record OpenPayment(Offer Offer) : ICommand;
public sealed record PaymentOpened(Offer Offer) : IDomainEvent;
public sealed record RecordCapture(Offer Offer, string Receipt) : ICommand;
public sealed record PaymentCaptured(Offer Offer, string Receipt) : IDomainEvent;
public sealed record ReconcilePayment(Offer Offer, int Review) : ICommand;
public sealed record PaymentReconciliationRequested(Offer Offer, int Review) : IDomainEvent;

public sealed record Payment(Offer? Offer, PaymentPhase Phase, string? Receipt, int Review) : IState<Payment>
{
    public static Payment Initial { get; } = new(null, PaymentPhase.Empty, null, 0);
    public Result<PaymentCaptured, RuleError> Capture(string receipt) =>
        Offer is null ? RuleError.WrongOwner : string.IsNullOrWhiteSpace(receipt) ? RuleError.InvalidInput
        : Receipt is not null && Receipt != receipt ? RuleError.ConflictingReceipt : new PaymentCaptured(Offer, receipt);
    public Payment Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        PaymentOpened e => this with { Offer = e.Offer, Phase = PaymentPhase.Pending },
        PaymentCaptured e => this with { Phase = PaymentPhase.Captured, Receipt = e.Receipt },
        PaymentReconciliationRequested e => this with { Review = e.Review },
        _ => this,
    };
}

public sealed class OpenPaymentSlice : Slice<OpenPayment, Payment, PaymentOpened, RuleError>
{
    public override object KeyOf(OpenPayment command) => command.Offer.SubscriptionId;
    public override Result<PaymentOpened, RuleError> Decide(OpenPayment command, Payment state) =>
        !command.Offer.IsValid ? RuleError.InvalidInput : state.Phase == PaymentPhase.Empty
            ? new PaymentOpened(command.Offer) : RuleError.AlreadyExists;
}

public sealed class RecordCaptureSlice : Slice<RecordCapture, Payment, PaymentCaptured, RuleError>
{
    public override object KeyOf(RecordCapture command) => command.Offer.SubscriptionId;
    public override Result<PaymentCaptured, RuleError> Decide(RecordCapture command, Payment state) =>
        state.Offer != command.Offer ? RuleError.WrongOwner : state.Capture(command.Receipt);
}

public sealed class ReconcilePaymentSlice : Slice<ReconcilePayment, Payment, PaymentReconciliationRequested, RuleError>
{
    public override object KeyOf(ReconcilePayment command) => command.Offer.SubscriptionId;
    public override Result<PaymentReconciliationRequested, RuleError> Decide(ReconcilePayment command, Payment state) =>
        state.Offer != command.Offer ? RuleError.WrongOwner : command.Review <= state.Review ? RuleError.StaleAttempt
            : new PaymentReconciliationRequested(command.Offer, command.Review);
}

public sealed record RecordProviderCapture(string SubscriptionId, string Receipt) : ICommand;
public sealed class RecordProviderCaptureSlice : Slice<RecordProviderCapture, Payment, PaymentCaptured, RuleError>
{
    public override object KeyOf(RecordProviderCapture command) => command.SubscriptionId;
    public override Result<PaymentCaptured, RuleError> Decide(RecordProviderCapture command, Payment state) => state.Capture(command.Receipt);
}
