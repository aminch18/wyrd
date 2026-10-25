using System.Net;
using Telecom.Infrastructure;

namespace Telecom.Tests;

public class HttpProviderSpecs
{
    [Fact]
    public async Task http_adapter_reads_actual_aspnet_camel_case_receipts_and_propagates_the_delivery_key()
    {
        var key = Guid.NewGuid();
        using var handler = new ReceiptHandler(key);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://provider/") };
        var adapter = new HttpProviders(http, http, http);
        var reply = await adapter.CallAsync(CapturePaymentIntegration.PaymentRequest(BusinessSpecs.Offer()), key, CancellationToken.None);
        Assert.Equal("external-receipt", reply.Receipt);
        Assert.Null(reply.Error);
    }

    private sealed class ReceiptHandler(Guid key) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(key.ToString(), Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            Assert.Contains("logicalKey", await request.Content!.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"receipt\":\"external-receipt\",\"error\":null}", System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
