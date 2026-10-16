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

    public bool IsOk { get; }

    public TResult Match<TResult>(Func<TOk, TResult> ok, Func<TError, TResult> error)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ArgumentNullException.ThrowIfNull(error);
        return IsOk ? ok(_value!) : error(_error!);
    }

    public static implicit operator Result<TOk, TError>(TOk value) => new(value);

    public static implicit operator Result<TOk, TError>(TError error) => new(error);

    public override string ToString() => IsOk ? $"Ok({_value})" : $"Error({_error})";
}
