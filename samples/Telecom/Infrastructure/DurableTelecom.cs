using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wyrd;
using Wyrd.EntityFrameworkCore;
using Wyrd.Runtime;

namespace Telecom.Infrastructure;

public static class DurableTelecom
{
    // Demo operating assumptions, not legal or regulatory retention advice.
    public static Retention Retention { get; } = new()
    {
        Deduplication = Keep.For(30.Days),
        FinishedInstances = Keep.For(90.Days),
        RelayedMessages = Keep.For(7.Days),
        Events = Keep.For(90.Days),
        DeadLetters = Keep.For(90.Days),
    };

    public static void AddTelecom(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Telecom")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Telecom.");

        services.AddDbContextFactory<TelecomDbContext>(options => options.UseNpgsql(connection));
        services.AddHttpClient("payments", client => client.BaseAddress = ProviderUri(configuration, "Payments"));
        services.AddHttpClient("mnp", client => client.BaseAddress = ProviderUri(configuration, "Mnp"));
        services.AddHttpClient("esim", client => client.BaseAddress = ProviderUri(configuration, "Esim"));

        services.AddSingleton<IProviderApi>(CreateProviders);
        services.AddSingleton(CreateRuntime);
        services.AddSingleton<IWyrdRuntime>(provider => provider.GetRequiredService<EfCoreRuntime<TelecomDbContext>>());
    }

    private static HttpProviders CreateProviders(IServiceProvider services)
    {
        var clients = services.GetRequiredService<IHttpClientFactory>();
        var payments = clients.CreateClient("payments");
        var mnp = clients.CreateClient("mnp");
        var esim = clients.CreateClient("esim");
        return new HttpProviders(payments, mnp, esim);
    }

    private static EfCoreRuntime<TelecomDbContext> CreateRuntime(IServiceProvider services)
    {
        var contexts = services.GetRequiredService<IDbContextFactory<TelecomDbContext>>();
        var providers = services.GetRequiredService<IProviderApi>();
        var runtime = new EfCoreRuntime<TelecomDbContext>(contexts, TelecomJson.Default, Retention);
        return TelecomModule.Register(runtime, providers);
    }

    private static Uri ProviderUri(IConfiguration configuration, string name)
    {
        var address = configuration[$"Providers:{name}"]
            ?? throw new InvalidOperationException($"Set Providers__{name}.");
        return new Uri(address);
    }
}
