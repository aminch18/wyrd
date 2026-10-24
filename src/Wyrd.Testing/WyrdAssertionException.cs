namespace Wyrd.Testing;

/// <summary>A scenario expectation that did not hold. Any test framework reports it as a failure.</summary>
public sealed class WyrdAssertionException : Exception
{
    /// <summary>Creates a failed Wyrd testing assertion without a custom message.</summary>
    public WyrdAssertionException()
    {
    }

    /// <summary>Creates a failed Wyrd testing assertion with its diagnostic explanation.</summary>
    public WyrdAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a failed assertion that preserves the underlying error for diagnosis.</summary>
    public WyrdAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
