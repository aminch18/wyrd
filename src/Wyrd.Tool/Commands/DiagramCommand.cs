using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Wyrd.Tool;

internal sealed class DiagramCommand : Command<DiagramCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public DiagramCommand(IAnsiConsole console) => _console = console;

    public sealed class Settings : DocumentSettings
    {
        [CommandOption("--assembly <PATH>", isRequired: true)]
        [Description("Compiled assembly containing public static Wyrd processes.")]
        public string AssemblyPath { get; init; } = "";

        public override ValidationResult Validate() => File.Exists(AssemblyPath)
            ? ValidationResult.Success()
            : ValidationResult.Error($"Assembly not found: {AssemblyPath}");
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        DocumentOutput.Write(Diagrams.Generate(settings.AssemblyPath), settings.OutputPath, _console);
}
