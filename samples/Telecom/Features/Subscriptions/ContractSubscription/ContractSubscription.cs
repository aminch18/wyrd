using Wyrd;

namespace Telecom;

public sealed record ContractSubscription(Offer Offer) : ICommand;

public sealed record SubscriptionOpened(Offer Offer) : IDomainEvent;

public sealed class ContractSubscriptionSlice : Slice<ContractSubscription, Subscription, SubscriptionOpened, RuleError>
{
    public override object KeyOf(ContractSubscription command) => command.Offer.SubscriptionId;

    public override Result<SubscriptionOpened, RuleError> Decide(ContractSubscription command, Subscription state)
    {
        var error = Validate(command, state);
        if (error is not null)
        {
            return error.Value;
        }

        return new SubscriptionOpened(command.Offer);
    }

    private static RuleError? Validate(ContractSubscription command, Subscription state)
    {
        if (!command.Offer.IsValid)
        {
            return RuleError.InvalidInput;
        }

        if (state is not Subscription.Empty)
        {
            return RuleError.AlreadyExists;
        }

        return null;
    }
}
