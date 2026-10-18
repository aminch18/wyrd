using System.Reflection;
using System.Runtime.Loader;

namespace Wyrd.Tool;

/// <summary>Discover public static processes in an isolated, collectible assembly context.</summary>
internal static class ProcessDiscovery
{
    public static T Read<T>(string assemblyPath, Func<string, IReadOnlyList<ProcessMember>, T> read)
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
            var processes = new List<ProcessMember>();
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

            return read(assembly.GetName().Name!, processes.OrderBy(p => p.Name, StringComparer.Ordinal)
                .ThenBy(p => p.Source, StringComparer.Ordinal).ToArray());
        }
        finally
        {
            context.Unload();
        }
    }

    private static bool IsProcess(Type type) => type.FullName == "Wyrd.Processes.Process"
        && type.Assembly.GetName().Name == "Wyrd";

    private static void Add(object? value, string source, List<ProcessMember> processes)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"Process member '{source}' returned null.");
        }

        var name = value.GetType().GetProperty("Name")?.GetValue(value) as string
            ?? throw new InvalidOperationException($"Process member '{source}' has no name.");
        processes.Add(new ProcessMember(name, source, value));
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

internal sealed record ProcessMember(string Name, string Source, object Value);
