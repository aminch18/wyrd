using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Wyrd.Runtime;

namespace Wyrd.Tool;

internal sealed class ReplayCommand : Command<ReplayCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public ReplayCommand(IAnsiConsole console) => _console = console;

    public sealed class Settings : DocumentSettings
    {
        [CommandArgument(0, "<HISTORY>")]
        [Description("Instance history exported with InstanceHistory.ToJson().")]
        public string HistoryPath { get; init; } = "";

        [CommandOption("--as-test")]
        [Description("Write an xUnit test that replays the history with Urd, instead of printing its timeline.")]
        public bool AsTest { get; init; }

        [CommandOption("--runtime <EXPRESSION>")]
        [Description("C# expression of the runtime factory, e.g. ShopModule.CreateRuntime. Required with --as-test.")]
        public string? Runtime { get; init; }

        [CommandOption("--process <EXPRESSION>")]
        [Description("C# expression of the process, e.g. Processes.OrderFulfillment. Required with --as-test.")]
        public string? Process { get; init; }

        [CommandOption("--namespace <NAMESPACE>")]
        [Description("Namespace of the generated test.")]
        [DefaultValue("Replays")]
        public string Namespace { get; init; } = "Replays";

        [CommandOption("--using <NAMESPACE>")]
        [Description("Extra using directive for the generated test (repeatable), e.g. Shop.Fulfillment.")]
        public string[] Usings { get; init; } = [];

        public override ValidationResult Validate()
        {
            if (!File.Exists(HistoryPath))
            {
                return ValidationResult.Error($"History not found: {HistoryPath}");
            }

            return AsTest && (Runtime is null || Process is null)
                ? ValidationResult.Error("--as-test needs --runtime and --process: the test calls Urd.ReplayAsync(runtime, process, history).")
                : ValidationResult.Success();
        }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var json = File.ReadAllText(settings.HistoryPath);
        var history = InstanceHistory.FromJson(json);
        var document = settings.AsTest
            ? ReplayTest.Generate(history, json, settings.Runtime!, settings.Process!, settings.Namespace, settings.Usings, Path.GetFileName(settings.HistoryPath))
            : ReplayTest.Timeline(history);
        return DocumentOutput.Write(document, settings.OutputPath, _console);
    }
}
