using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using MinApiLib.Endpoints;
using Telecom.Features.Operations;
using Telecom.Features.Subscriptions;
using Wyrd.Runtime;

namespace Telecom.Tests;

public class EndpointSpecs
{
    [Fact]
    public async Task minapilib_discovers_the_slices_and_preserves_the_subscription_http_flow()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await using var app = CreateApi(runtime);
        await app.StartAsync();
        using var http = ClientFor(app);
        var offer = BusinessSpecs.Offer();

        using var missing = await http.GetAsync("/subscriptions/unknown");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var contracted = await http.PostAsJsonAsync("/subscriptions", offer);
        Assert.Equal(HttpStatusCode.Accepted, contracted.StatusCode);
        using var duplicate = await http.PostAsJsonAsync("/subscriptions", offer);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var progress = await http.GetFromJsonAsync<CustomerProgress>("/subscriptions/S1");
        Assert.True(progress!.Provisioned);
        Assert.False(progress.Operational);

        using var completed = await http.PostAsJsonAsync("/webhooks/mnp/completed", new PortCompletedV1("S1", offer.Msisdn, offer.Epoch));
        Assert.Equal(HttpStatusCode.Accepted, completed.StatusCode);
        var beforeProbe = await http.GetFromJsonAsync<OperationDetail>("/operations/S1");
        Assert.False(beforeProbe!.PaymentAndBilling!.BillingStarted);

        using var connected = await http.PostAsJsonAsync("/webhooks/connectivity", new ConnectivityVerifiedV1("S1", "P1", 1));
        Assert.Equal(HttpStatusCode.Accepted, connected.StatusCode);
        var afterProbe = await http.GetFromJsonAsync<OperationDetail>("/operations/S1");
        Assert.True(afterProbe!.PaymentAndBilling!.BillingStarted);
        Assert.Equal(ProcessStatus.Completed, afterProbe.Profile);

        using var history = await http.GetAsync("/histories/profiles/S1/P1/1");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.NotEmpty(InstanceHistory.FromJson(await history.Content.ReadAsStringAsync()).Entries);
        await app.StopAsync();
    }

    [Fact]
    public async Task command_endpoints_keep_not_found_and_domain_rejections_explicit()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await using var app = CreateApi(runtime);
        await app.StartAsync();
        using var http = ClientFor(app);

        using var unknown = await http.PostAsync("/subscriptions/unknown/profiles/P2/attempts/2", null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var contracted = await http.PostAsJsonAsync("/subscriptions", BusinessSpecs.Offer());
        Assert.Equal(HttpStatusCode.Accepted, contracted.StatusCode);
        using var stale = await http.PostAsync("/subscriptions/S1/profiles/P2/attempts/1", null);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var replacement = await http.PostAsync("/subscriptions/S1/profiles/P2/attempts/2", null);
        Assert.Equal(HttpStatusCode.Accepted, replacement.StatusCode);
        using var reconciliation = await http.PostAsync("/subscriptions/S1/reconcile/1", null);
        Assert.Equal(HttpStatusCode.Accepted, reconciliation.StatusCode);
        using var cancellation = await http.PostAsync("/subscriptions/S1/cancel-port", null);
        Assert.Equal(HttpStatusCode.Accepted, cancellation.StatusCode);
        await app.StopAsync();
    }

    [Theory]
    [InlineData("/webhooks/payments", "{\"subscriptionId\":null,\"receipt\":\"r\"}")]
    [InlineData("/webhooks/payments", "{\"subscriptionId\":\"S1\",\"receipt\":\" \"}")]
    [InlineData("/webhooks/mnp/completed", "{\"subscriptionId\":\"S1\",\"msisdn\":null,\"epoch\":1}")]
    [InlineData("/webhooks/mnp/completed", "{\"subscriptionId\":\"S/1\",\"msisdn\":\"+34600000001\",\"epoch\":1}")]
    [InlineData("/webhooks/mnp/cancelled", "{\"subscriptionId\":\"S1\",\"msisdn\":\"+34600000001\",\"epoch\":0}")]
    [InlineData("/webhooks/mnp/confirmed", "{\"subscriptionId\":\"S1\",\"msisdn\":\"34600000001\",\"epoch\":1}")]
    [InlineData("/webhooks/mnp/confirmed", "{\"subscriptionId\":\"S1\",\"msisdn\":\"+34600000001\",\"epoch\":1,\"windowStart\":\"2026-10-26T04:00:00Z\",\"windowEnd\":\"2026-10-26T02:00:00Z\"}")]
    [InlineData("/webhooks/connectivity", "{\"subscriptionId\":null,\"profileId\":\"P1\",\"attempt\":1}")]
    [InlineData("/webhooks/connectivity", "{\"subscriptionId\":\"S1\",\"profileId\":\"P/1\",\"attempt\":1}")]
    [InlineData("/webhooks/connectivity", "{\"subscriptionId\":\"S1\",\"profileId\":\"P1\",\"attempt\":0}")]
    public async Task malformed_webhooks_are_rejected_before_starting_processes(string path, string payload)
    {
        using var runtime = TelecomModule.CreateRuntime();
        await using var app = CreateApi(runtime);
        await app.StartAsync();
        using var http = ClientFor(app);
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(path, content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(runtime.Journal);
        await app.StopAsync();
    }

    [Fact]
    public async Task invalid_offers_return_bad_request_and_payment_reviews_cannot_skip_a_number()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await using var app = CreateApi(runtime);
        await app.StartAsync();
        using var http = ClientFor(app);
        var offer = BusinessSpecs.Offer();
        using var invalid = await http.PostAsJsonAsync("/subscriptions", offer with { InitialAmount = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("InvalidInput", (await invalid.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("error").GetString());
        using var contracted = await http.PostAsJsonAsync("/subscriptions", offer);
        Assert.Equal(HttpStatusCode.Accepted, contracted.StatusCode);
        using var skipped = await http.PostAsync("/subscriptions/S1/reconcile/2147483647", null);
        Assert.Equal(HttpStatusCode.Conflict, skipped.StatusCode);
        using var first = await http.PostAsync("/subscriptions/S1/reconcile/1", null);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        using var duplicate = await http.PostAsync("/subscriptions/S1/reconcile/1", null);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var second = await http.PostAsync("/subscriptions/S1/reconcile/2", null);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(2, runtime.StateOf<Payment>("S1").Review);
        await app.StopAsync();
    }

    [Fact]
    public async Task operator_incidents_require_an_existing_subscription_and_support_zero_attempts()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await using var app = CreateApi(runtime);
        await app.StartAsync();
        using var http = ClientFor(app);
        using var unknown = await http.PostAsync("/incidents/unknown/payment/0/open", null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Empty(runtime.Views<IncidentItem>());
        using var contracted = await http.PostAsJsonAsync("/subscriptions", BusinessSpecs.Offer());
        Assert.Equal(HttpStatusCode.Accepted, contracted.StatusCode);
        using var incident = await http.PostAsync("/incidents/S1/payment/0/open", null);
        Assert.Equal(HttpStatusCode.Accepted, incident.StatusCode);
        var item = Assert.Single(runtime.Views<IncidentItem>());
        Assert.Equal(new IncidentItem("S1", "payment", 0, true), item);
        await app.StopAsync();
    }

    private static WebApplication CreateApi(IWyrdRuntime runtime)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(runtime);
        var app = builder.Build();
        app.MapEndpoints(typeof(ContractSubscriptionEndpoint).Assembly);
        return app;
    }

    private static HttpClient ClientFor(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()!;
        return new HttpClient { BaseAddress = new Uri(Assert.Single(addresses.Addresses)) };
    }
}
