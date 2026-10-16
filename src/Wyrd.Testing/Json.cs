using Wyrd.Runtime;

namespace Wyrd.Testing;

/// <summary>Compares and round-trips values the way Wyrd stores them: as JSON of their declared type.</summary>
internal static class Json
{
    private static readonly MessageTypes Types = MessageTypes.Reflection();

    public static string Of(object value) => Types.Serialize(value, value.GetType()).Payload;

    public static bool SameAs(object? expected, object? actual) =>
        expected is null || actual is null
            ? expected is null && actual is null
            : expected.GetType() == actual.GetType() && Of(expected) == Of(actual);

    /// <summary>Fails if a value would not come back identical after being stored as <paramref name="declared"/>.</summary>
    public static void EnsureRoundTrips(object value, Type declared, string what)
    {
        string before;
        object after;
        try
        {
            var (type, payload) = Types.Serialize(value, declared);
            before = payload;
            after = Types.Deserialize(type, payload);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or System.Text.Json.JsonException)
        {
            throw new WyrdAssertionException(
                $"{what} {value.GetType().Name} cannot be stored: it does not survive a JSON round trip ({ex.Message}). Keep states and messages as plain data: records, primitives, collections of those — no interfaces.", ex);
        }

        if (after.GetType() != value.GetType() || Of(after) != Of(value) || Types.Serialize(after, declared).Payload != before)
        {
            throw new WyrdAssertionException(
                $"{what} {value.GetType().Name} changes when stored: {before} came back as {Types.Serialize(after, declared).Payload}.");
        }
    }
}
