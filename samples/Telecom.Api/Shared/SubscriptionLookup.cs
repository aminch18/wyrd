using Wyrd.Runtime;

namespace Telecom.Api.Shared;

internal static class SubscriptionLookup
{
    public static async Task<Offer?> FindOfferAsync(IWyrdRuntime runtime, string id, CancellationToken cancellationToken)
    {
        var subscription = await runtime.StateOfAsync<Subscription>(id, cancellationToken);
        if (subscription is Subscription.Contracted contracted)
        {
            return contracted.Offer;
        }

        return null;
    }
}
