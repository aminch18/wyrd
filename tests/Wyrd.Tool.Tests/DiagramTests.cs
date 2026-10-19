using Wyrd.Processes;

namespace Wyrd.Tool.Tests;

public class DiagramTests
{
    [Fact]
    public void renders_two_processes_from_the_test_assembly()
    {
        var document = Diagrams.Generate(typeof(TestProcesses).Assembly.Location);

        Assert.Contains("# Wyrd.Tool.Tests processes", document, StringComparison.Ordinal);
        Assert.Contains("## First", document, StringComparison.Ordinal);
        Assert.Contains("## Second", document, StringComparison.Ordinal);
        Assert.Equal(2, document.Split("```mermaid", StringSplitOptions.None).Length - 1);
        Assert.Contains("flowchart TD", document, StringComparison.Ordinal);
        Assert.True(document.IndexOf("## First", StringComparison.Ordinal)
            < document.IndexOf("## Second", StringComparison.Ordinal));
    }
}

public sealed record TestEvent(string Id) : IEvent;
public sealed record TestCommand(string Id) : ICommand;

public static class TestProcesses
{
    public static Process First
    {
        get
        {
            // Only the stdio transport test opts into a noisy process getter.
            if (Environment.GetEnvironmentVariable("WYRD_MCP_TEST_STDOUT") == "1")
            {
                Console.WriteLine("Process discovery diagnostic");
            }

            return Process.StartOn<TestEvent>(e => e.Id).Then(e => new TestCommand(e.Id));
        }
    }
    public static readonly Process Second = Process.StartOn<TestEvent>(e => e.Id).Then(e => new TestCommand(e.Id));
}
