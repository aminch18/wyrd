using Wyrd.Processes;
using Wyrd;

namespace Telecom;

public sealed record BeginBilling(string SubscriptionId, string ProfileId, int Attempt) : ICommand;

public sealed record BillingBegan(string SubscriptionId, string ProfileId, int Attempt) : IDomainEvent;

public sealed class BeginBillingSlice : Slice<BeginBilling, Billing, BillingBegan, RuleError>
{
    public override object KeyOf(BeginBilling command) => command.SubscriptionId;

    public override Result<BillingBegan, RuleError> Decide(BeginBilling command, Billing state)
    {
        var error = Validate(state);
        if (error is not null)
        {
            return error.Value;
        }

        return new BillingBegan(command.SubscriptionId, command.ProfileId, command.Attempt);
    }

    private static RuleError? Validate(Billing state)
    {
        if (state.Started)
        {
            return RuleError.AlreadyExists;
        }

        return null;
    }
}

public static partial class Flows
{
    public static Process BillingCommencement => Process.StartOn<ServiceOperationalV1>(e => e.SubscriptionId)
        .Then(Bill).Retry(3.Times);

    private static BeginBilling Bill(ServiceOperationalV1 operational) => new(operational.SubscriptionId, operational.ProfileId, operational.Attempt);
}
