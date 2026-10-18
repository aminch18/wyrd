using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;

namespace Wyrd.Tool.Tests;

public class CommandTests
{
    [Fact]
    public void diagram_requires_an_assembly_option()
    {
        var (app, errors) = CreateApp();

        var result = app.Run("diagram");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("assembly", errors.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("asyncapi", "--contracts", "Contract snapshot not found")]
    [InlineData("diagram", "--assembly", "Assembly not found")]
    public void nonexistent_input_is_reported_to_stderr(string command, string option, string message)
    {
        var (app, errors) = CreateApp();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = app.Run(command, option, missing);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(message, errors.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void help_describes_commands_and_options()
    {
        var (app, errors) = CreateApp();

        var result = app.Run("asyncapi", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--contracts", result.Output, StringComparison.Ordinal);
        Assert.Contains("--output", result.Output, StringComparison.Ordinal);
        Assert.Contains("--title", result.Output, StringComparison.Ordinal);
        Assert.Empty(errors.Output);
    }

    [Fact]
    public void asyncapi_stdout_contains_only_the_document_and_applies_metadata_options()
    {
        var (app, errors) = CreateApp();
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", "contracts.wyrd.txt");

        var result = app.Run("asyncapi", "--contracts", path, "--title", "My API [v1]", "--version", "2.0");

        Assert.Equal(0, result.ExitCode);
        var expected = AsyncApi.Generate(Contracts.Parse(File.ReadAllText(path)), "My API [v1]", "2.0");
        Assert.Equal(expected, app.Console.Output);
        Assert.Empty(errors.Output);
    }

    [Fact]
    public void malformed_snapshot_reports_the_line_to_stderr()
    {
        var (app, errors) = CreateApp();
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "# comment\ninvalid");

            var result = app.Run("asyncapi", "--contracts", path);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains("line 2", errors.Output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (CommandAppTester App, TestConsole Errors) CreateApp()
    {
        var errors = new TestConsole();
        var app = new CommandAppTester();
        app.Configure(config => Program.Configure(config, errors));
        return (app, errors);
    }
}
