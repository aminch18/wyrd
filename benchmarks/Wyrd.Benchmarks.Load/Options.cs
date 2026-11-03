using System.Globalization;

namespace Wyrd.Benchmarks.Load;

/// <summary>The <c>--name value</c> options of a role, with their defaults.</summary>
internal sealed class Options(string[] args)
{
    public string? Text(string name)
    {
        var index = Array.IndexOf(args, "--" + name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    public int Number(string name, int fallback) =>
        Text(name) is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    public double Decimal(string name, double fallback) =>
        Text(name) is { } value ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;
}
