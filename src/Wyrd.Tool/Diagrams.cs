using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace Wyrd.Tool;

internal static class Diagrams
{
    public static string Generate(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Assembly not found", fullPath);
        }

        var context = new ProcessAssemblyContext(fullPath);
        try
        {
            var assembly = context.LoadFromAssemblyPath(fullPath);
            var processes = new List<(string Name, string Source, string Mermaid)>();
            foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (var property in type.GetProperties(flags))
                {
                    if (property.GetMethod is { IsPublic: true, IsStatic: true }
                        && property.GetIndexParameters().Length == 0 && IsProcess(property.PropertyType))
                    {
                        Add(property.GetValue(null), $"{type.FullName}.{property.Name}", processes);
                    }
                }

                foreach (var field in type.GetFields(flags))
                {
                    if (IsProcess(field.FieldType))
                    {
                        Add(field.GetValue(null), $"{type.FullName}.{field.Name}", processes);
                    }
                }
            }

            var markdown = new StringBuilder().Append("# ").Append(assembly.GetName().Name).AppendLine(" processes").AppendLine();
            foreach (var process in processes.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Source, StringComparer.Ordinal))
            {
                markdown.Append("## ").AppendLine(process.Name).AppendLine();
                markdown.AppendLine("```mermaid");
                markdown.Append(process.Mermaid.TrimEnd()).AppendLine();
                markdown.AppendLine("```").AppendLine();
            }

            return markdown.ToString().TrimEnd() + "\n";
        }
        finally
        {
            context.Unload();
        }
    }

    private static bool IsProcess(Type type) => type.FullName == "Wyrd.Processes.Process"
        && type.Assembly.GetName().Name == "Wyrd";

    private static void Add(object? value, string source, List<(string Name, string Source, string Mermaid)> processes)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"Process member '{source}' returned null.");
        }

        var type = value.GetType();
        var name = (string?)type.GetProperty("Name")?.GetValue(value);
        var mermaid = (string?)type.GetMethod("ToMermaid", Type.EmptyTypes)?.Invoke(value, null);
        if (name is null || mermaid is null)
        {
            throw new InvalidOperationException($"Process member '{source}' could not be rendered.");
        }

        processes.Add((name, source, mermaid));
    }

    private sealed class ProcessAssemblyContext(string assemblyPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);
        private readonly string _directory = Path.GetDirectoryName(assemblyPath)!;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName)
                ?? Path.Combine(_directory, assemblyName.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
