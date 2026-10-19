using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Wyrd.Tool;

internal sealed class ProcessesCommand(IAnsiConsole console) : Command<ProcessesCommand.Settings>
{
    public sealed class Settings : DocumentSettings
    {
        [CommandOption("--assembly <PATH>", isRequired: true)]
        [Description("Compiled assembly containing public static Wyrd processes.")]
        public string AssemblyPath { get; init; } = "";

        [CommandOption("--check <PATH>")]
        [Description("Compare with a process snapshot; exit 1 for changed, added or removed processes.")]
        public string? CheckPath { get; init; }

        public override ValidationResult Validate()
        {
            if (!File.Exists(AssemblyPath))
            {
                return ValidationResult.Error($"Assembly not found: {AssemblyPath}");
            }

            if (CheckPath is not null && OutputPath is not null)
            {
                return ValidationResult.Error("Use either --output or --check, not both.");
            }

            return CheckPath is not null && !File.Exists(CheckPath)
                ? ValidationResult.Error($"Process snapshot not found: {CheckPath}. Generate it with --output.")
                : ValidationResult.Success();
        }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var current = Processes.Discover(settings.AssemblyPath);
        if (settings.CheckPath is not null)
        {
            var check = Processes.Check(Processes.Parse(File.ReadAllText(settings.CheckPath)), current);
            console.Profile.Out.Writer.Write(check.Report);
            return check.ExitCode;
        }

        return DocumentOutput.Write(Processes.Generate(current), settings.OutputPath, console);
    }
}
