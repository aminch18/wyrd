namespace Wyrd;

/// <summary>
/// The outcome of a decision: either a value or a domain error. No exceptions for expected failures.
/// Both sides convert implicitly, so a decider can simply return <c>new OrderPlaced(...)</c> or <c>OrderError.EmptyOrder</c>.
/// </summary>
public readonly record struct Result<TOk, TError>
{
    private readonly TOk? _value;
    private readonly TError? _error;

    private Result(TOk value)
    {
        _value = value;
        IsOk = true;
    }

    private Result(TError error) => _error = error;

    /// <summary>Whether this result contains an accepted value rather than a domain error.</summary>
    public bool IsOk { get; }

    /// <summary>Calls the matching function for the accepted value or domain error and returns its result.</summary>
    /// <example><code>decision.Match(value =&gt; value.ToString(), error =&gt; error.ToString())</code></example>
    public TResult Match<TResult>(Func<TOk, TResult> ok, Func<TError, TResult> error)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ArgumentNullException.ThrowIfNull(error);
        return IsOk ? ok(_value!) : error(_error!);
    }

    /// <summary>Wraps an accepted value so a decider can return its event directly.</summary>
    public static implicit operator Result<TOk, TError>(TOk value) => new(value);

    /// <summary>Wraps a business rejection so a decider can return its error directly.</summary>
    public static implicit operator Result<TOk, TError>(TError error) => new(error);

    /// <summary>Formats the accepted value as <c>Ok(...)</c> or the rejection as <c>Error(...)</c>.</summary>
    public override string ToString() => IsOk ? $"Ok({_value})" : $"Error({_error})";
}
