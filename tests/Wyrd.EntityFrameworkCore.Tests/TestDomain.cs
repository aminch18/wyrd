using Microsoft.EntityFrameworkCore;
using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.EntityFrameworkCore.Tests;

// An application with tables of its own, and Wyrd's next to them.

public sealed class Note
{
    public int Id { get; set; }

    public string Text { get; set; } = "";
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>(e => e.ToTable("notes"));
        modelBuilder.AddWyrd();
    }
}

// A small domain with bugs on purpose: the retry layer on a durable host.

public sealed record StartJob(string Id) : ICommand;

public sealed record JobStarted(string Id) : IDomainEvent;

public sealed record RunJob(string Id) : ICommand;

public sealed record JobRan(string Id) : IDomainEvent;

public sealed record JobState : IState<JobState>
{
    public static JobState Initial { get; } = new();

    public JobState Evolve(IDomainEvent domainEvent) => this;
}

public sealed class StartJobSlice : Slice<StartJob, JobState, JobStarted, string>
{
    public override object KeyOf(StartJob command) => command.Id;

    public override Result<JobStarted, string> Decide(StartJob command, JobState state) => new JobStarted(command.Id);
}

/// <summary>A slice with a bug: the host can't handle its command.</summary>
public sealed class BrokenSlice : Slice<RunJob, JobState, JobRan, string>
{
    public override object KeyOf(RunJob command) => command.Id;

    public override Result<JobRan, string> Decide(RunJob command, JobState state) => throw new InvalidOperationException("bug in RunJob");
}

public sealed record JobView(string Id);

/// <summary>A projection with a bug: its event can never be handled, and no process is there to decide.</summary>
public sealed class BrokenJobs : Projection<JobView>
{
    public BrokenJobs() => On<JobStarted>(e => e.Id, (_, _) => throw new InvalidOperationException("bug in BrokenJobs"));
}

public static class JobSteps
{
    public static RunJob Run(JobStarted started) => new(started.Id);
}

public static class Jobs
{
    public static Process Process { get; } = Wyrd.Processes.Process.StartOn<JobStarted>(e => e.Id, "Job")
        .Then(JobSteps.Run).Retry(2.Times, Backoff.Immediate);
}

// Another service: it hears that orders were shipped, and counts every delivery it handles.

public sealed record Shipment(string OrderId, int Deliveries);

public sealed class Shipments : Projection<Shipment>
{
    public Shipments() =>
        On<Shop.Contracts.OrderShippedV1>(e => e.OrderId, (view, e) => (view ?? new Shipment(e.OrderId, 0)) with { Deliveries = (view?.Deliveries ?? 0) + 1 });
}

/// <summary>Records what it is given; thread-safe, so concurrent relays can share it. Fails while <see cref="Down"/>.</summary>
public sealed class RecordingTransport : IWyrdTransport
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<TransportMessage> _sent = new();

    public bool Down { get; set; }

    public IReadOnlyList<TransportMessage> Sent => [.. _sent];

    public Task SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        if (Down)
        {
            throw new IOException("broker unreachable");
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>A read model added to a shop that already has stock: it catches up from the stock items' states.</summary>
public sealed record StockLevel(string Sku, int Available);

public sealed class StockLevels : Projection<StockLevel>
{
    public StockLevels()
    {
        Seed<Shop.Inventory.StockItem>((sku, _) => sku, (_, sku, item) => new StockLevel(sku, item.Available));
        On<Shop.Inventory.StockAdded>(e => e.Sku, (view, e) => new StockLevel(e.Sku, (view?.Available ?? 0) + e.Quantity));
    }
}

/// <summary>All the shop's stock in one view: its seed folds every stock item, so a second seed would show.</summary>
public sealed record TotalStock(int Available);

public sealed class TotalStocks : Projection<TotalStock>
{
    public TotalStocks()
    {
        Seed<Shop.Inventory.StockItem>((_, _) => "all", (view, _, item) => new TotalStock((view?.Available ?? 0) + item.Available));
        On<Shop.Inventory.StockAdded>(_ => "all", (view, e) => new TotalStock((view?.Available ?? 0) + e.Quantity));
    }
}

/// <summary>Every restock of the shop in one view, from as many streams as there are stock items.</summary>
public sealed record AllStock(int Restocked);

public sealed class AllStocks : Projection<AllStock>
{
    public AllStocks() =>
        On<Shop.Inventory.StockAdded>(_ => "all", (view, e) => new AllStock((view?.Restocked ?? 0) + e.Quantity));
}
