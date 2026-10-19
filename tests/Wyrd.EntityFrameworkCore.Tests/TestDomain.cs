using Microsoft.EntityFrameworkCore;
using Wyrd.Processes;

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
