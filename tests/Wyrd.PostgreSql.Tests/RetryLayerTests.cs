using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.PostgreSql.Tests;

public sealed record StartJob(string Id) : ICommand;

public sealed record JobStarted(string Id) : IDomainEvent;

public sealed record RunJob(string Id) : ICommand;

public sealed record JobRan(string Id) : IDomainEvent;

public sealed record Jobs : IState<Jobs>
{
    public static Jobs Initial { get; } = new();

    public Jobs Evolve(IDomainEvent domainEvent) => this;
}

public sealed class StartJobSlice : Slice<StartJob, Jobs, JobStarted, string>
{
    public override object KeyOf(StartJob command) => command.Id;

    public override Result<JobStarted, string> Decide(StartJob command, Jobs state) => new JobStarted(command.Id);
}

/// <summary>A slice with a bug: the host can't handle its command.</summary>
public sealed class BrokenSlice : Slice<RunJob, Jobs, JobRan, string>
{
    public override object KeyOf(RunJob command) => command.Id;

    public override Result<JobRan, string> Decide(RunJob command, Jobs state) => throw new InvalidOperationException("bug in RunJob");
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

[Collection(PostgresDatabase.Name)]
public class RetryLayerTests(PostgresFixture postgres)
{
    private static readonly Process Job = Process.StartOn<JobStarted>(e => e.Id, "Job")
        .Then(JobSteps.Run).Retry(2.Times, Backoff.Immediate);

    [Fact]
    public async Task a_process_command_the_host_cant_handle_reaches_its_process_as_a_failure()
    {
#pragma warning disable IL2026, IL3050 // reflection JSON keeps the test domain small
        var runtime = new PostgresRuntime(postgres.DataSource, postgres.NewSchema())
            .AddSlice(new StartJobSlice())
            .AddSlice(new BrokenSlice())
            .AddProcess(Job);
#pragma warning restore IL2026, IL3050
        await runtime.EnsureSchemaAsync();

        await runtime.SendAsync(new StartJob("J-1"));
        for (var i = 0; i < 20 && await runtime.ProcessPendingAsync() > 0; i++)
        {
        }

        // Three attempts (one + Retry(2.Times)), decided by the process, then suspended with a reason: no dead letter,
        // and no hidden redeliveries multiplying the attempts.
        Assert.Equal(ProcessStatus.Suspended, await runtime.StatusOfAsync(Job, "J-1"));
        var history = (await runtime.HistoryOfAsync(Job, "J-1"))!;
        Assert.Equal(3, history.Entries.Count(e => e.Kind == HistoryKind.Sent));
        Assert.Equal("JobSteps.Run failed after 3 attempts", history.Entries.Last(e => e.Kind == HistoryKind.Suspended).Reason);
        Assert.Equal((0, 0), await runtime.BacklogAsync());
    }

    [Fact]
    public async Task a_message_no_process_decides_on_ends_in_the_dead_letters_without_blocking_the_queue()
    {
#pragma warning disable IL2026, IL3050 // reflection JSON keeps the test domain small
        var runtime = new PostgresRuntime(postgres.DataSource, postgres.NewSchema() with { MaxDeliveryAttempts = 3 })
            .AddSlice(new StartJobSlice())
            .AddProjection(new BrokenJobs());
#pragma warning restore IL2026, IL3050
        await runtime.EnsureSchemaAsync();

        await runtime.SendAsync(new StartJob("J-1"));
        Assert.IsType<Decision.Accepted>(await runtime.SendAsync(new StartJob("J-2"))); // work behind it still flows
        var quietUntil = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < quietUntil)
        {
            if (await runtime.ProcessPendingAsync() == 0)
            {
                await Task.Delay(20);
            }
        }

        Assert.Equal((0, 2), await runtime.BacklogAsync()); // both JobStarted events, after 3 attempts each
    }
}
