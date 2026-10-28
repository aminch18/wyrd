using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using MinApiLib.Endpoints;
using Telecom.Features.Workspace;
using Wyrd.Runtime;

namespace Telecom.Tests;

public class WorkspaceSpecs
{
    [Fact]
    public async Task workspace_reads_show_the_billing_gate_and_do_not_grant_authority_to_stale_actions()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await using var app = CreateApi(runtime);
        await app.StartAsync();
        using var http = ClientFor(app);
        var offer = BusinessSpecs.Offer();

        using var missing = await http.GetAsync("/bff/subscriptions/unknown");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var created = await http.PostAsJsonAsync("/subscriptions", offer);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);

        var beforePort = await http.GetFromJsonAsync<SubscriptionWorkspace>("/bff/subscriptions/S1");
        Assert.True(beforePort!.Actions.CancelPort.Enabled);
        Assert.True(beforePort.Actions.ReplaceProfile.Enabled);
        Assert.Equal(offer.CustomerId, beforePort.Progress.CustomerId);
        Assert.Equal(offer.InitialAmount, beforePort.Progress.InitialAmount);
        Assert.False(beforePort.Payment!.BillingStarted);
        Assert.Contains(beforePort.Processes, process => process.Name == "ProfileProvisioning" && process.Status == "Awaiting");
        Assert.Contains(beforePort.Processes, process => process.Name == "PaymentCollection" && process.History.Count > 0);

        using var ported = await http.PostAsJsonAsync("/webhooks/mnp/completed", new PortCompletedV1("S1", offer.Msisdn, offer.Epoch));
        Assert.Equal(HttpStatusCode.Accepted, ported.StatusCode);
        // A client that kept the earlier enabled button is still rejected by the owner.
        using var staleCancellation = await http.PostAsync("/subscriptions/S1/cancel-port", null);
        Assert.Equal(HttpStatusCode.Conflict, staleCancellation.StatusCode);

        var awaitingProbe = await http.GetFromJsonAsync<SubscriptionWorkspace>("/bff/subscriptions/S1");
        Assert.True(awaitingProbe!.Progress.Ported);
        Assert.False(awaitingProbe.Progress.Connected);
        Assert.False(awaitingProbe.Payment!.BillingStarted);
        Assert.False(awaitingProbe.Actions.CancelPort.Enabled);
        Assert.False(awaitingProbe.Model.Activation.Ready);

        using var connected = await http.PostAsJsonAsync("/webhooks/connectivity", new ConnectivityVerifiedV1("S1", "P1", 1));
        Assert.Equal(HttpStatusCode.Accepted, connected.StatusCode);
        var operational = await http.GetFromJsonAsync<SubscriptionWorkspace>("/bff/subscriptions/S1");
        Assert.True(operational!.Progress.Operational);
        Assert.True(operational.Payment!.BillingStarted);
        Assert.True(operational.Model.Activation.Ready);
        Assert.False(operational.Actions.ReplaceProfile.Enabled);
        var list = await http.GetFromJsonAsync<SubscriptionCard[]>("/bff/subscriptions");
        Assert.True(Assert.Single(list!).Payment!.BillingStarted);
        await app.StopAsync();
    }

    [Fact]
    public async Task operator_incidents_and_replacement_are_visible_without_fabricating_activation()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await using var app = CreateApi(runtime);
        await app.StartAsync();
        using var http = ClientFor(app);
        using var created = await http.PostAsJsonAsync("/subscriptions", BusinessSpecs.Offer());
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        using var flagged = await http.PostAsync("/incidents/S1/connectivity/1/open", null);
        Assert.Equal(HttpStatusCode.Accepted, flagged.StatusCode);
        var affected = await http.GetFromJsonAsync<SubscriptionWorkspace>("/bff/subscriptions/S1");
        Assert.Single(affected!.Incidents);
        Assert.Equal(1, Assert.Single((await http.GetFromJsonAsync<SubscriptionCard[]>("/bff/subscriptions"))!).OpenIncidents);

        var nextProfile = affected.Actions.SuggestedProfile;
        using var replaced = await http.PostAsync($"/subscriptions/S1/profiles/{nextProfile}/attempts/2", null);
        Assert.Equal(HttpStatusCode.Accepted, replaced.StatusCode);
        using var oldProbe = await http.PostAsJsonAsync("/webhooks/connectivity", new ConnectivityVerifiedV1("S1", "P1", 1));
        Assert.Equal(HttpStatusCode.Accepted, oldProbe.StatusCode);
        var afterOldProbe = await http.GetFromJsonAsync<SubscriptionWorkspace>("/bff/subscriptions/S1");
        Assert.Equal(2, afterOldProbe!.Progress.Attempt);
        Assert.False(afterOldProbe.Progress.Connected);
        Assert.False(afterOldProbe.Progress.Operational);

        using var resolved = await http.PostAsync("/incidents/S1/connectivity/1/resolve", null);
        Assert.Equal(HttpStatusCode.Accepted, resolved.StatusCode);
        var afterResolve = await http.GetFromJsonAsync<SubscriptionWorkspace>("/bff/subscriptions/S1");
        Assert.Empty(afterResolve!.Incidents);
        Assert.False(afterResolve.Progress.Operational);
        await app.StopAsync();
    }

    [Fact]
    public async Task workspace_assets_are_embedded_and_only_known_files_are_served()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await using var app = CreateApi(runtime);
        await app.StartAsync();
        using var http = ClientFor(app);
        using var page = await http.GetAsync("/");
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
        Assert.Contains("/workspace/app.js", await page.Content.ReadAsStringAsync());
        foreach (var name in new[] { "app.js", "render.js", "api.js", "styles.css" })
        {
            using var asset = await http.GetAsync($"/workspace/{name}");
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        }
        using var unknown = await http.GetAsync("/workspace/TelecomJson.cs");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        await app.StopAsync();
    }

    private static WebApplication CreateApi(IWyrdRuntime runtime)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(runtime);
        var app = builder.Build();
        app.MapEndpoints(typeof(WorkspaceQueries).Assembly);
        return app;
    }

    private static HttpClient ClientFor(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        return new HttpClient { BaseAddress = new Uri(address) };
    }
}
