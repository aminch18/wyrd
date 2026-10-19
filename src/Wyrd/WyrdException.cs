namespace Wyrd;

/// <summary>
/// A failure a host or an application can handle: retry it, report it, dead-letter the message or answer an operator.
/// It derives from <see cref="InvalidOperationException"/>, so code that caught that before still does. Mistakes in how
/// an application is put together (two owners for one command, a step that can't be called) and invalid arguments keep
/// the BCL exceptions: they are fixed, not handled.
/// </summary>
public class WyrdException : InvalidOperationException
{
    public WyrdException(string message)
        : base(message)
    {
    }

    public WyrdException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Another transaction changed the same state, instance or read model first. Retrying is always safe: the engine
/// reloads and decides again. Hosts retry on their own; you see it only when Wyrd joins your transaction.
/// </summary>
public sealed class ConcurrencyConflictException : WyrdException
{
    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A message names a type nothing registered here uses. Register the slice, publication, projection or process that
/// uses it — or, for a message from another service, check the contract name and version.
/// </summary>
public sealed class UnknownMessageTypeException(string messageType)
    : WyrdException($"Unknown message type '{messageType}'. Register the slice, publication, projection or process that uses it.")
{
    /// <summary>The message type as it was stored or received.</summary>
    public string MessageType { get; } = messageType;
}

/// <summary>A type has no JSON metadata in the application's <c>[WyrdJsonContext]</c>.</summary>
public sealed class MissingJsonMetadataException(Type type, Exception innerException)
    : WyrdException($"{type.Name} has no JSON metadata. Add [JsonSerializable(typeof({type.Name}))] to your [WyrdJsonContext].", innerException)
{
    public Type Type { get; } = type;
}

/// <summary>A subscriber expects a newer version of a contract than was published, and no upcaster bridges the gap.</summary>
public sealed class MissingUpcasterException(string message) : WyrdException(message);

/// <summary>Something other than an integration event was offered across a service boundary.</summary>
public sealed class NotAnIntegrationEventException(string messageType)
    : WyrdException($"{messageType} is not an integration event: only integration events cross service boundaries.")
{
    public string MessageType { get; } = messageType;
}

/// <summary>
/// An operator asked a process instance for something it can't do now: it doesn't exist, isn't suspended, was already
/// compensating, or started with another definition of its process.
/// </summary>
public sealed class InstanceStateException(string process, string key, string message) : WyrdException(message)
{
    public string Process { get; } = process;

    public string Key { get; } = key;
}
