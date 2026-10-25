using System.Text.Json.Serialization;
using Wyrd;

namespace Telecom;

public sealed record Offer(string SubscriptionId, string CustomerId, string Msisdn, long Epoch, string ProfileId, decimal InitialAmount)
{
    public bool IsValid => ValidId(SubscriptionId) && ValidId(CustomerId) && ValidId(ProfileId)
        && Msisdn is { Length: >= 9 and <= 16 } && Msisdn[0] == '+' && Msisdn[1] != '0' && Msisdn[1..].All(char.IsAsciiDigit)
        && Epoch > 0 && InitialAmount > 0;

    // Keys are also URL segments and components of deterministic provider operation keys.
    public static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

public enum RuleError { InvalidInput, AlreadyExists, WrongOwner, StaleAttempt, IllegalTransition, Irreversible, NotReady, ConflictingReceipt }

public sealed record ContractSubscription(Offer Offer) : ICommand;
public sealed record SubscriptionOpened(Offer Offer) : IDomainEvent;
public sealed record DeclineSubscription(string SubscriptionId) : ICommand;
public sealed record SubscriptionDeclined(string SubscriptionId) : IDomainEvent;

[Union]
[JsonDerivedType(typeof(Empty), "empty")]
[JsonDerivedType(typeof(Contracted), "contracted")]
[JsonDerivedType(typeof(Declined), "declined")]
public abstract record Subscription : IState<Subscription>
{
    public static Subscription Initial { get; } = new Empty();
    public Subscription Evolve(IDomainEvent domainEvent) => (this, domainEvent) switch
    {
        (Empty, SubscriptionOpened e) => new Contracted(e.Offer),
        (Contracted, SubscriptionDeclined) => new Declined(),
        _ => this,
    };
    public sealed record Empty : Subscription;
    public sealed record Contracted(Offer Offer) : Subscription;
    public sealed record Declined : Subscription;
}

public sealed class ContractSubscriptionSlice : Slice<ContractSubscription, Subscription, SubscriptionOpened, RuleError>
{
    public override object KeyOf(ContractSubscription command) => command.Offer.SubscriptionId;
    public override Result<SubscriptionOpened, RuleError> Decide(ContractSubscription command, Subscription state) =>
        !command.Offer.IsValid ? RuleError.InvalidInput : state is Subscription.Empty
            ? new SubscriptionOpened(command.Offer) : RuleError.AlreadyExists;
}

public sealed class DeclineSubscriptionSlice : Slice<DeclineSubscription, Subscription, SubscriptionDeclined, RuleError>
{
    public override object KeyOf(DeclineSubscription command) => command.SubscriptionId;
    public override Result<SubscriptionDeclined, RuleError> Decide(DeclineSubscription command, Subscription state) =>
        state is Subscription.Empty ? RuleError.IllegalTransition : new SubscriptionDeclined(command.SubscriptionId);
}

public sealed record AdmitSubscription(Offer Offer) : ICommand;
public sealed record SubscriptionAdmitted(Offer Offer) : IDomainEvent;
public sealed class AdmitSubscriptionSlice : Slice<AdmitSubscription, NumberPort, SubscriptionAdmitted, RuleError>
{
    public override object KeyOf(AdmitSubscription command) => command.Offer.Msisdn;
    public override Result<SubscriptionAdmitted, RuleError> Decide(AdmitSubscription command, NumberPort state) =>
        state.Owner != command.Offer ? RuleError.WrongOwner
        : state.Phase != PortPhase.Reserved ? RuleError.IllegalTransition
        // A sample credit policy, deliberately checked before any provider is contacted.
        : command.Offer.InitialAmount > 250m ? RuleError.InvalidInput : new SubscriptionAdmitted(command.Offer);
}
