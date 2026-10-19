using System.Globalization;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;
using Wyrd.Processes;

namespace Wyrd.Tool.Tests;

public class ProcessesTests
{
    [Fact]
    public void discovers_the_same_public_properties_and_fields_as_diagrams()
    {
        var snapshots = Processes.Discover(typeof(TestProcesses).Assembly.Location);

        Assert.Equal(["First", "Second"], snapshots.Select(p => p.Name));
        Assert.Equal(TestProcesses.First.Fingerprint, snapshots[0].Fingerprint);
        Assert.Equal(TestProcesses.Second.Fingerprint, snapshots[1].Fingerprint);
    }

    [Fact]
    public void unchanged_snapshot_passes_after_round_trip()
    {
        var snapshot = Capture(Example());
        var text = Processes.Generate([snapshot]);

        Assert.StartsWith("# wyrd processes 1\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.Equal(text, Processes.Generate(Processes.Parse(text)));
        var check = Processes.Check(Processes.Parse(text), [snapshot]);
        Assert.Equal(0, check.ExitCode);
        Assert.Contains("matches", check.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void readable_lines_match_the_exact_fingerprinted_structure()
    {
        var snapshot = Capture(Example());

        Assert.Equal(Example().Fingerprint, snapshot.Fingerprint);
        Assert.Equal(["trigger TestEvent", "step ProcessesTests.Reserve ~ ProcessesTests.Release",
            "step ProcessesTests.Charge ~ ProcessesTests.Refund", "await Confirmed", "outcome ProcessesTests.Cancel"], snapshot.Structure);
    }

    [Fact]
    public void changing_only_retry_undo_outcome_and_time_configuration_is_compatible()
    {
        Process adjusted = Process.StartOn<TestEvent>(e => e.Id, "Checkout")
            .Then(Reserve).Retry(9.Times, Backoff.Linear).OrCompensate().Undo(Release, 20.Times)
            .Then(Charge).NoRetry("The provider retries independently.").Undo(Refund, 30.Times)
            .Await<Confirmed>(e => e.Id).Within(60.Minutes)
            .OnCompensated(Cancel).Retry(20.Times, Backoff.Linear);
        var previous = Capture(Example());
        var current = Capture(adjusted);

        Assert.Equal(previous.Fingerprint, current.Fingerprint);
        Assert.Equal(Processes.Generate([previous]), Processes.Generate([current]));
        Assert.Equal(0, Processes.Check([previous], [current]).ExitCode);
    }

    [Fact]
    public void reordering_steps_reports_old_and_new_structure_and_recovery()
    {
        Process reordered = Process.StartOn<TestEvent>(e => e.Id, "Checkout")
            .Then(Charge).Retry(5.Times).Undo(Refund)
            .Then(Reserve).Retry(5.Times).Undo(Release)
            .Await<Confirmed>(e => e.Id).Within(15.Minutes)
            .OnCompensated(Cancel).Retry(5.Times);
        var check = Processes.Check([Capture(Example())], [Capture(reordered)]);

        Assert.Equal(1, check.ExitCode);
        Assert.Contains("Changed process: Checkout", check.Report, StringComparison.Ordinal);
        Assert.Contains("Old: " + Example().Fingerprint, check.Report, StringComparison.Ordinal);
        Assert.Contains("New: " + reordered.Fingerprint, check.Report, StringComparison.Ordinal);
        Assert.Contains("in-flight instances", check.Report, StringComparison.Ordinal);
        Assert.Contains("keep the old definition", check.Report, StringComparison.Ordinal);
        Assert.Contains("--output", check.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void changing_trigger_step_undo_wait_or_outcome_is_breaking()
    {
        Process[] variants =
        [
            Process.StartOn<Confirmed>(e => e.Id, "Checkout").Then(e => new TestCommand(e.Id)).NoRetry("Test only."),
            Process.StartOn<TestEvent>(e => e.Id, "Checkout").Then(Charge).Retry(5.Times).Undo(Release)
                .Then(Charge).Retry(5.Times).Undo(Refund).Await<Confirmed>(e => e.Id).Within(15.Minutes).OnCompensated(Cancel),
            Process.StartOn<TestEvent>(e => e.Id, "Checkout").Then(Reserve).Retry(5.Times).Undo(Refund)
                .Then(Charge).Retry(5.Times).Undo(Refund).Await<Confirmed>(e => e.Id).Within(15.Minutes).OnCompensated(Cancel),
            Process.StartOn<TestEvent>(e => e.Id, "Checkout").Then(Reserve).Retry(5.Times).Undo(Release)
                .Then(Charge).Retry(5.Times).Undo(Refund).Await<TestEvent>(e => e.Id).Within(15.Minutes).OnCompensated(Cancel),
            Process.StartOn<TestEvent>(e => e.Id, "Checkout").Then(Reserve).Retry(5.Times).Undo(Release)
                .Then(Charge).Retry(5.Times).Undo(Refund).Await<Confirmed>(e => e.Id).Within(15.Minutes).OnCompensated(Refund),
        ];

        foreach (var variant in variants)
        {
            Assert.Equal(1, Processes.Check([Capture(Example())], [Capture(variant)]).ExitCode);
        }
    }

    [Fact]
    public void added_and_removed_processes_require_review()
    {
        var previous = Capture(Example("Removed"));
        var current = Capture(Example("Added"));
        var check = Processes.Check([previous], [current]);

        Assert.Equal(1, check.ExitCode);
        Assert.Contains("Added process: Added\n  Old: (absent)", check.Report, StringComparison.Ordinal);
        Assert.Contains("Removed process: Removed", check.Report, StringComparison.Ordinal);
        Assert.Contains("New: (absent)", check.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void output_is_sorted_and_independent_of_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = Capture(Example("I"));
            var second = Capture(Example("z"));
            Assert.Equal(Processes.Generate([first, second]), Processes.Generate([second, first]));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("# wyrd processes 2\n[]")]
    [InlineData("# wyrd processes 1\n")]
    [InlineData("# wyrd processes 1\n- name: broken")]
    [InlineData("# wyrd processes 1\n- null")]
    [InlineData("# wyrd processes 1\n- name: first\n  name: second")]
    public void invalid_or_unknown_snapshot_formats_are_rejected(string text) =>
        Assert.ThrowsAny<Exception>(() => Processes.Parse(text));

    [Fact]
    public void a_new_fingerprint_algorithm_requires_a_tool_update()
    {
        var unsupported = new { Trigger = typeof(TestEvent), Nodes = Array.Empty<ProcessNode>(), Fingerprint = "000000000000" };
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Processes.Capture(new ProcessMember("Future", "test", unsupported)));
        Assert.Contains("Update Wyrd.Tool", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void process_names_round_trip_without_changing_snapshot_boundaries()
    {
        var snapshot = Capture(Example("Checkout: \"quoted\"\nnext line"));
        var parsed = Processes.Parse(Processes.Generate([snapshot]));
        Assert.Equal(snapshot.Name, Assert.Single(parsed).Name);
        Assert.Equal(0, Processes.Check([snapshot], parsed).ExitCode);
    }

    [Fact]
    public void duplicate_process_names_are_rejected()
    {
        var snapshot = Capture(Example());
        var exception = Assert.Throws<FormatException>(() => Processes.Generate([snapshot, snapshot]));
        Assert.Contains("Duplicate process name 'Checkout'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void empty_assemblies_can_have_an_explicit_empty_snapshot()
    {
        Assert.Empty(Processes.Parse(Processes.Generate([])));
        Assert.Equal(0, Processes.Check([], []).ExitCode);
    }

    [Fact]
    public void command_writes_and_checks_the_generated_file_without_modifying_it()
    {
        var path = Path.GetTempFileName();
        try
        {
            var (write, errors) = CreateApp();
            var result = write.Run("processes", "--assembly", typeof(TestProcesses).Assembly.Location, "--output", path);
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Empty(errors.Output);
            var expected = File.ReadAllText(path);
            var (check, checkErrors) = CreateApp();
            result = check.Run("processes", "--assembly", typeof(TestProcesses).Assembly.Location, "--check", path);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("matches", result.Output, StringComparison.Ordinal);
            Assert.Equal(expected, File.ReadAllText(path));
            Assert.Empty(checkErrors.Output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void command_reports_changes_and_returns_one()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Processes.Generate([Capture(Example("Previous"))]));
            var (app, errors) = CreateApp();
            var result = app.Run("processes", "--assembly", typeof(TestProcesses).Assembly.Location, "--check", path);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Added process: First", result.Output, StringComparison.Ordinal);
            Assert.Contains("Removed process: Previous", result.Output, StringComparison.Ordinal);
            Assert.Empty(errors.Output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void output_and_check_cannot_be_used_together()
    {
        var (app, errors) = CreateApp();
        var result = app.Run("processes", "--assembly", typeof(TestProcesses).Assembly.Location, "--output", "snapshot", "--check", "snapshot");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("either --output or --check", errors.Output, StringComparison.Ordinal);
    }

    private static ProcessSnapshot Capture(Process process) => Processes.Capture(new ProcessMember(process.Name, "test", process));

    private static Process Example(string name = "Checkout") => Process.StartOn<TestEvent>(e => e.Id, name)
        .Then(Reserve).Retry(5.Times).Undo(Release)
        .Then(Charge).Retry(5.Times).Undo(Refund)
        .Await<Confirmed>(e => e.Id).Within(15.Minutes)
        .OnCompensated(Cancel).Retry(5.Times);

    private static TestCommand Reserve(TestEvent e) => new(e.Id);
    private static TestCommand Release(TestEvent e) => new(e.Id);
    private static TestCommand Charge(TestEvent e) => new(e.Id);
    private static TestCommand Refund(TestEvent e) => new(e.Id);
    private static TestCommand Cancel(TestEvent e) => new(e.Id);

    private sealed record Confirmed(string Id) : IEvent;

    private static (CommandAppTester App, TestConsole Errors) CreateApp()
    {
        var errors = new TestConsole();
        var app = new CommandAppTester();
        app.Configure(config => Program.Configure(config, errors));
        return (app, errors);
    }
}
