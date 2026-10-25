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
