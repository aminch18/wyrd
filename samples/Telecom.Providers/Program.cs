using Microsoft.EntityFrameworkCore;
using MinApiLib.Endpoints;
using Telecom.Providers;
using Telecom.Providers.Persistence;

var builder = WebApplication.CreateBuilder(args);
var providerName = builder.Configuration["ProviderKind"]
    ?? throw new InvalidOperationException("Set ProviderKind: payments, mnp or esim.");
var loseFirstReply = builder.Configuration.GetValue<bool>("LoseFirstReply");

builder.Services.AddSingleton(new ProviderSettings(providerName, loseFirstReply));
builder.Services.AddDbContextFactory<ProviderDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Provider")));

var app = builder.Build();
var contexts = app.Services.GetRequiredService<IDbContextFactory<ProviderDb>>();
await using (var database = await contexts.CreateDbContextAsync())
{
    await database.Database.EnsureCreatedAsync();
}

app.MapEndpoints();
app.Run();
