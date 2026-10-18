using System.Globalization;

namespace Wyrd.Runtime;

/// <summary>Keys travel in messages and rows, so they are normalised to invariant strings.</summary>
internal static class Keys
{
    public static string Of(object key) => key switch
    {
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => key.ToString() ?? throw new InvalidOperationException("A key cannot be null."),
    };
}
