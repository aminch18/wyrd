using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Wyrd.Tool;

internal sealed class AsyncApiCommand : Command<AsyncApiCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public AsyncApiCommand(IAnsiConsole console) => _console = console;

    public sealed class Settings : DocumentSettings
    {
        [CommandOption("--contracts <PATH>")]
        [Description("Integration contract snapshot to read.")]
        [DefaultValue("contracts.wyrd.txt")]
        public string ContractsPath { get; init; } = "contracts.wyrd.txt";

        [CommandOption("--title <TITLE>")]
        [Description("Document title. Defaults to the snapshot's folder name.")]
        public string? Title { get; init; }

        [CommandOption("--version <VERSION>")]
        [Description("Version of the documented API.")]
        [DefaultValue("0.0.0")]
        public string Version { get; init; } = "0.0.0";

        public override ValidationResult Validate() => File.Exists(ContractsPath)
            ? ValidationResult.Success()
            : ValidationResult.Error($"Contract snapshot not found: {ContractsPath}");
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(settings.ContractsPath);
        var title = settings.Title ?? new FileInfo(path).Directory!.Name;
        var contracts = Contracts.Parse(File.ReadAllText(path));
        var document = AsyncApi.Generate(contracts, title, settings.Version);
        return DocumentOutput.Write(document, settings.OutputPath, _console);
    }
}
