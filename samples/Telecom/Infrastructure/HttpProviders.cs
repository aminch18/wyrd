using System.Net.Http.Json;

namespace Telecom.Infrastructure;

public sealed class HttpProviders(HttpClient payments, HttpClient mnp, HttpClient esim) : IProviderApi
{
    public async Task<ProviderReply> CallAsync(ProviderRequest request, Guid deliveryKey, CancellationToken cancellationToken)
    {
        var client = request.Action switch
        {
            ProviderAction.Capture or ProviderAction.FindCapture => payments,
            ProviderAction.SubmitPort or ProviderAction.CancelPort => mnp,
            _ => esim,
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, "operations")
        {
            Content = JsonContent.Create(request, ProviderJson.Default.ProviderRequest),
        };
        message.Headers.Add("Idempotency-Key", deliveryKey.ToString());
        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(ProviderJson.Default.ProviderReply, cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("Provider returned no receipt.");
    }
}
