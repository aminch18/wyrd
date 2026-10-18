using System.ComponentModel;
using Spectre.Console.Cli;

namespace Wyrd.Tool;

internal abstract class DocumentSettings : CommandSettings
{
    [CommandOption("-o|--output <PATH>")]
    [Description("Write the document to this file. Defaults to standard output.")]
    public string? OutputPath { get; init; }
}
