using Wyrd;

namespace Telecom;

public enum ActivationFact { Payment, PortCompleted, Provisioned, Connectivity }
public sealed record AssignProfile(string SubscriptionId, string CustomerId, string ProfileId, int Attempt) : ICommand;
public sealed record ProfileAssigned(string SubscriptionId, string CustomerId, string ProfileId, int Attempt) : IDomainEvent;
public sealed record ClaimProfile(string SubscriptionId, string CustomerId, string ProfileId, int Attempt) : ICommand;
public sealed record ProfileClaimed(string SubscriptionId, string CustomerId, string ProfileId, int Attempt) : IDomainEvent;
public sealed record ObserveActivation(string SubscriptionId, ActivationFact Fact, string ProfileId = "", int Attempt = 0) : ICommand;
public sealed record ActivationObserved(string SubscriptionId, string EvidenceKey, Activation Next) : IDomainEvent;
public sealed record AssessActivation(string SubscriptionId) : ICommand;
public sealed record ServiceOperational(string SubscriptionId, string ProfileId, int Attempt) : IDomainEvent;

// A single resource owner. Profiles are quarantined after replacement; reuse needs a real deactivation protocol.
public sealed record ProfileLease(string? SubscriptionId, string? CustomerId, int Attempt) : IState<ProfileLease>
{
    public static ProfileLease Initial { get; } = new(null, null, 0);
    public ProfileLease Evolve(IDomainEvent domainEvent) => domainEvent is ProfileClaimed e
        ? new(e.SubscriptionId, e.CustomerId, e.Attempt) : this;
}

public sealed class ClaimProfileSlice : Slice<ClaimProfile, ProfileLease, ProfileClaimed, RuleError>
{
    public override object KeyOf(ClaimProfile command) => command.ProfileId;
    public override Result<ProfileClaimed, RuleError> Decide(ClaimProfile command, ProfileLease state) =>
        !Offer.ValidId(command.SubscriptionId) || !Offer.ValidId(command.ProfileId) || !Offer.ValidId(command.CustomerId) || command.Attempt < 1 ? RuleError.InvalidInput
        : state.SubscriptionId is not null && (state.SubscriptionId != command.SubscriptionId || state.CustomerId != command.CustomerId || state.Attempt != command.Attempt)
            ? RuleError.AlreadyExists : new ProfileClaimed(command.SubscriptionId, command.CustomerId, command.ProfileId, command.Attempt);
}

public sealed record Activation(bool Paid, bool Ported, string? CustomerId, string? ProfileId, int Attempt,
    bool Provisioned, bool Connected, bool Operational) : IState<Activation>
{
    public static Activation Initial { get; } = new(false, false, null, null, 0, false, false, false);
    public bool Ready => Paid && Ported && ProfileId is not null && Provisioned && Connected;
    public Activation Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        ProfileAssigned e => this with { CustomerId = e.CustomerId, ProfileId = e.ProfileId, Attempt = e.Attempt, Provisioned = false, Connected = false },
        ActivationObserved e => e.Next,
        ServiceOperational => this with { Operational = true },
        _ => this,
    };
}

public sealed class AssignProfileSlice : Slice<AssignProfile, Activation, ProfileAssigned, RuleError>
{
    public override object KeyOf(AssignProfile command) => command.SubscriptionId;
    public override Result<ProfileAssigned, RuleError> Decide(AssignProfile command, Activation state) =>
        !Offer.ValidId(command.SubscriptionId) || !Offer.ValidId(command.ProfileId) || !Offer.ValidId(command.CustomerId) ? RuleError.InvalidInput
        : state.Operational ? RuleError.Irreversible
        : command.Attempt <= state.Attempt || command.Attempt != state.Attempt + 1 ? RuleError.StaleAttempt
        : state.ProfileId == command.ProfileId ? RuleError.IllegalTransition
        : state.CustomerId is not null && state.CustomerId != command.CustomerId ? RuleError.WrongOwner
        : new ProfileAssigned(command.SubscriptionId, command.CustomerId, command.ProfileId, command.Attempt);
}

public sealed class ObserveActivationSlice : Slice<ObserveActivation, Activation, ActivationObserved, RuleError>
{
    public override object KeyOf(ObserveActivation command) => command.SubscriptionId;
    public override Result<ActivationObserved, RuleError> Decide(ObserveActivation command, Activation state)
    {
        if (command.Fact is ActivationFact.Provisioned or ActivationFact.Connectivity
            && (command.Attempt != state.Attempt || command.ProfileId != state.ProfileId)) return RuleError.StaleAttempt;
        var next = command.Fact switch
        {
            ActivationFact.Payment => state with { Paid = true },
            ActivationFact.PortCompleted => state with { Ported = true },
            ActivationFact.Provisioned => state with { Provisioned = true },
            ActivationFact.Connectivity => state with { Connected = true },
            _ => state,
        };
        return new ActivationObserved(command.SubscriptionId, $"{command.SubscriptionId}/{command.Fact}/{command.Attempt}", next);
    }
}

public sealed class AssessActivationSlice : Slice<AssessActivation, Activation, ServiceOperational, RuleError>
{
    public override object KeyOf(AssessActivation command) => command.SubscriptionId;
    public override Result<ServiceOperational, RuleError> Decide(AssessActivation command, Activation state) =>
        state.Operational ? RuleError.AlreadyExists : !state.Ready ? RuleError.NotReady : new ServiceOperational(command.SubscriptionId, state.ProfileId!, state.Attempt);
}
