namespace Wyrd.Tool;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is not ("asyncapi" or "diagram"))
            {
                throw new ArgumentException("Usage: dotnet wyrd asyncapi [--contracts PATH] [--output PATH] [--title TITLE] [--version VERSION] | dotnet wyrd diagram --assembly PATH [--output PATH]");
            }

            var command = args[0];
            var options = ParseOptions(args[1..], command == "asyncapi"
                ? ["--contracts", "--output", "--title", "--version"]
                : ["--assembly", "--output"]);
            string document;
            if (command == "asyncapi")
            {
                var contractsPath = Path.GetFullPath(GetOption(options, "--contracts", "contracts.wyrd.txt"));
                var title = GetOption(options, "--title", Path.GetFileName(Path.GetDirectoryName(contractsPath))!);
                var version = GetOption(options, "--version", "0.0.0");
                document = AsyncApi.Generate(Contracts.Parse(File.ReadAllText(contractsPath)), title, version);
            }
            else
            {
                if (!options.TryGetValue("--assembly", out var assemblyPath))
                {
                    throw new ArgumentException("The diagram command requires --assembly PATH.");
                }

                document = Diagrams.Generate(assemblyPath);
            }

            if (options.TryGetValue("--output", out var output))
            {
                File.WriteAllText(output, document);
            }
            else
            {
                Console.Out.Write(document);
            }

            return 0;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or IOException or UnauthorizedAccessException
            or System.Reflection.TargetInvocationException or System.Reflection.ReflectionTypeLoadException or InvalidOperationException)
        {
            Console.Error.WriteLine($"wyrd: {error.GetBaseException().Message}");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args, string[] allowed)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !allowed.Contains(args[i], StringComparer.Ordinal)
                || !options.TryAdd(args[i], args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Invalid option near '{args[i]}'.");
            }
        }

        return options;
    }

    private static string GetOption(Dictionary<string, string> options, string name, string fallback) =>
        options.TryGetValue(name, out var value) ? value : fallback;
}
