using Wyrd;

namespace Telecom;

public sealed record DeclineSubscription(string SubscriptionId) : ICommand;

public sealed record SubscriptionDeclined(string SubscriptionId) : IDomainEvent;

public sealed class DeclineSubscriptionSlice : Slice<DeclineSubscription, Subscription, SubscriptionDeclined, RuleError>
{
    public override object KeyOf(DeclineSubscription command) => command.SubscriptionId;

    public override Result<SubscriptionDeclined, RuleError> Decide(DeclineSubscription command, Subscription state)
    {
        var error = Validate(state);
        if (error is not null)
        {
            return error.Value;
        }

        return new SubscriptionDeclined(command.SubscriptionId);
    }

    private static RuleError? Validate(Subscription state)
    {
        if (state is Subscription.Empty)
        {
            return RuleError.IllegalTransition;
        }

        return null;
    }
}
