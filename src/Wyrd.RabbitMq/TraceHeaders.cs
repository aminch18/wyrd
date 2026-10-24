using System.Text;
using Wyrd.Runtime;

namespace Wyrd.RabbitMq;

/// <summary>The W3C trace context as message headers: diagnostic only, never part of a message's identity.</summary>
internal static class TraceHeaders
{
    private const string Parent = "traceparent";
    private const string State = "tracestate";

    public static Dictionary<string, object?>? Of(TransportMessage message)
    {
        if (message.TraceParent is null)
        {
            return null;
        }

        var headers = new Dictionary<string, object?> { [Parent] = message.TraceParent };
        if (message.TraceState is not null)
        {
            headers[State] = message.TraceState;
        }

        return headers;
    }

    public static TransportMessage On(TransportMessage message, IDictionary<string, object?>? headers) =>
        message with { TraceParent = Read(headers, Parent), TraceState = Read(headers, State) };

    // The client hands string headers back as bytes.
    private static string? Read(IDictionary<string, object?>? headers, string name) =>
        headers is not null && headers.TryGetValue(name, out var value)
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => null,
            }
            : null;
}
