using Wyrd;

namespace Telecom;

public sealed record ProvisionProfile(string SubscriptionId, string CustomerId, string ProfileId, int Attempt) : ICommand;

public sealed record ProvisionReceived(string SubscriptionId, string ProfileId, int Attempt, string Receipt) : IDomainEvent;

public sealed class ProvisionProfileIntegration(IProviderApi provider) : Integration<ProvisionProfile, ProvisionReceived, ProviderError>
{
    public override object KeyOf(ProvisionProfile command) => command.SubscriptionId;
    public override async Task<Result<ProvisionReceived, ProviderError>> ExecuteAsync(ProvisionProfile command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var reply = await provider.CallAsync(new(ProviderAction.Provision, $"profile/{command.ProfileId}/{command.Attempt}", command.ProfileId,
            command.CustomerId, 0, command.Attempt), context.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (reply.Error is not null)
        {
            return reply.Error.Value;
        }

        return new ProvisionReceived(command.SubscriptionId, command.ProfileId, command.Attempt, reply.Receipt!);
    }
}
