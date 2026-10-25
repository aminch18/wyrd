namespace Telecom;

/// <summary>The external payment, portability and eSIM operations used by integrations.</summary>
public interface IProviderApi
{
    Task<ProviderReply> CallAsync(ProviderRequest request, Guid deliveryKey, CancellationToken cancellationToken);
}
