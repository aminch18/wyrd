using System.Net;
using Telecom.Infrastructure;
using Wyrd;

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task every_integration_rejects_a_missing_success_receipt_as_a_technical_failure(string? receipt)
    {
        var provider = new FixedReplyProvider(new ProviderReply(receipt));
        var context = new IntegrationContext(Guid.NewGuid(), 1);
        var offer = BusinessSpecs.Offer();
        await Assert.ThrowsAsync<IOException>(async () => await new CapturePaymentIntegration(provider)
            .ExecuteAsync(new CapturePayment(offer), context, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(async () => await new FindPaymentIntegration(provider)
            .ExecuteAsync(new FindPayment(offer), context, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(async () => await new SubmitPortIntegration(provider)
            .ExecuteAsync(new SubmitPort(offer), context, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(async () => await new CancelPortIntegration(provider)
            .ExecuteAsync(new CancelPort(offer), context, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(async () => await new ProvisionProfileIntegration(provider)
            .ExecuteAsync(new ProvisionProfile("S1", "C1", "P1", 1), context, CancellationToken.None));
    }

    [Fact]
    public async Task a_provider_business_rejection_does_not_require_a_receipt()
    {
        var provider = new FixedReplyProvider(new ProviderReply(null, ProviderError.Declined));
        var result = await new CapturePaymentIntegration(provider).ExecuteAsync(
            new CapturePayment(BusinessSpecs.Offer()), new IntegrationContext(Guid.NewGuid(), 1), CancellationToken.None);
        Assert.False(result.IsOk);
        Assert.Equal(ProviderError.Declined, result.Match(_ => throw new InvalidOperationException("Expected rejection."), error => error));
    }

    private sealed class FixedReplyProvider(ProviderReply reply) : IProviderApi
    {
        public Task<ProviderReply> CallAsync(ProviderRequest request, Guid deliveryKey, CancellationToken cancellationToken) =>
            Task.FromResult(reply);
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
