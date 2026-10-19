using System.Text;

namespace Wyrd.Tool;

internal static class Diagrams
{
    public static string Generate(string assemblyPath) => ProcessDiscovery.Read(assemblyPath, (assemblyName, processes) =>
    {
        var markdown = new StringBuilder().Append("# ").Append(assemblyName).AppendLine(" processes").AppendLine();
        foreach (var process in processes)
        {
            var mermaid = process.Value.GetType().GetMethod("ToMermaid", Type.EmptyTypes)?.Invoke(process.Value, null) as string
                ?? throw new InvalidOperationException($"Process member '{process.Source}' could not be rendered.");
            markdown.Append("## ").AppendLine(process.Name).AppendLine();
            markdown.AppendLine("```mermaid");
            markdown.Append(mermaid.TrimEnd()).AppendLine();
            markdown.AppendLine("```").AppendLine();
        }

        return markdown.ToString().TrimEnd() + "\n";
    });
}
