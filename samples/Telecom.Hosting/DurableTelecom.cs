using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wyrd;
using Wyrd.EntityFrameworkCore;
using Wyrd.Runtime;

namespace Telecom.Hosting;

public sealed class TelecomDbContext(DbContextOptions<TelecomDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddWyrd();
}

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

public static class DurableTelecom
{
    // These are demo operating assumptions, not legal or regulatory retention advice.
    public static Retention Retention { get; } = new()
    {
        Deduplication = Keep.For(30.Days), FinishedInstances = Keep.For(90.Days),
        RelayedMessages = Keep.For(7.Days), Events = Keep.For(90.Days), DeadLetters = Keep.For(90.Days),
    };

    public static void AddTelecom(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Telecom")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Telecom.");
        services.AddDbContextFactory<TelecomDbContext>(o => o.UseNpgsql(connection));
        services.AddHttpClient("payments", c => c.BaseAddress = ProviderUri(configuration, "Payments"));
        services.AddHttpClient("mnp", c => c.BaseAddress = ProviderUri(configuration, "Mnp"));
        services.AddHttpClient("esim", c => c.BaseAddress = ProviderUri(configuration, "Esim"));
        services.AddSingleton<IProviderApi>(p => new HttpProviders(p.GetRequiredService<IHttpClientFactory>().CreateClient("payments"),
            p.GetRequiredService<IHttpClientFactory>().CreateClient("mnp"), p.GetRequiredService<IHttpClientFactory>().CreateClient("esim")));
        services.AddSingleton(p => TelecomModule.Register(new EfCoreRuntime<TelecomDbContext>(
            p.GetRequiredService<IDbContextFactory<TelecomDbContext>>(), TelecomJson.Default, Retention), p.GetRequiredService<IProviderApi>()));
        services.AddSingleton<IWyrdRuntime>(p => p.GetRequiredService<EfCoreRuntime<TelecomDbContext>>());
    }
    private static Uri ProviderUri(IConfiguration configuration, string name) => new(configuration[$"Providers:{name}"]
        ?? throw new InvalidOperationException($"Set Providers__{name}."));
}
