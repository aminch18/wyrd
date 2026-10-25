using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Telecom;

var builder = WebApplication.CreateBuilder(args);
var kind = builder.Configuration["ProviderKind"] ?? throw new InvalidOperationException("Set ProviderKind: payments, mnp or esim.");
var loseReply = builder.Configuration.GetValue<bool>("LoseFirstReply");
builder.Services.AddDbContextFactory<ProviderDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Provider")));
var app = builder.Build();
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<ProviderDb>>().CreateDbContextAsync())
    await db.Database.EnsureCreatedAsync();

app.MapGet("/health", () => Results.Ok());
app.MapPost("/operations", async (ProviderRequest request, HttpRequest http, IDbContextFactory<ProviderDb> factory, CancellationToken ct) =>
{
    var expected = request.Action switch { ProviderAction.Capture or ProviderAction.FindCapture => "payments", ProviderAction.Provision => "esim", _ => "mnp" };
    if (expected != kind || !Guid.TryParse(http.Headers["Idempotency-Key"], out var deliveryKey)) return Results.BadRequest();
    // Concurrent workers race on unique logical/resource keys. The losing insert rereads the winner.
    for (var retry = 0; retry < 3; retry++)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.Effects.FindAsync([request.LogicalKey], ct);
        if (request.Action == ProviderAction.FindCapture)
            return Results.Ok(existing is null ? new ProviderReply(null, ProviderError.NotFound) : new ProviderReply(existing.Receipt));
        var json = JsonSerializer.Serialize(request, TelecomJson.Default.ProviderRequest);
        if (existing is not null) return Results.Ok(existing.Request == json
            ? new ProviderReply(existing.Receipt) : new ProviderReply(null, ProviderError.Conflict));
        var resource = request.Action == ProviderAction.Provision ? request.Resource : null;
        if (resource is not null && await db.Effects.AnyAsync(e => e.Resource == resource, ct))
            return Results.Ok(new ProviderReply(null, ProviderError.Conflict));
        var effect = new ProviderEffect { Key = request.LogicalKey, Request = json, Resource = resource, Receipt = $"receipt-{request.LogicalKey}", DeliveryKey = deliveryKey };
        db.Effects.Add(effect);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (retry < 2) { continue; }
        // The effect is durable already. A 503 demonstrates a committed effect with no usable reply.
        return loseReply && request.Action is ProviderAction.Capture or ProviderAction.Provision
            ? Results.StatusCode(503) : Results.Ok(new ProviderReply(effect.Receipt));
    }
    return Results.StatusCode(503);
});
app.Run();

internal sealed class ProviderEffect
{
    public string Key { get; set; } = "";
    public string Request { get; set; } = "";
    public string Receipt { get; set; } = "";
    public string? Resource { get; set; }
    public Guid DeliveryKey { get; set; }
}
internal sealed class ProviderDb(DbContextOptions<ProviderDb> options) : DbContext(options)
{
    public DbSet<ProviderEffect> Effects => Set<ProviderEffect>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderEffect>().HasKey(e => e.Key);
        modelBuilder.Entity<ProviderEffect>().HasIndex(e => e.Resource).IsUnique();
    }
}
