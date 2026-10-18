using Spectre.Console;

namespace Wyrd.Tool;

internal static class DocumentOutput
{
    public static int Write(string document, string? path, IAnsiConsole console)
    {
        if (path is not null)
        {
            File.WriteAllText(path, document);
        }
        else
        {
            // Documents must remain byte-for-byte suitable for pipes and redirects.
            console.Profile.Out.Writer.Write(document);
        }

        return 0;
    }
}
