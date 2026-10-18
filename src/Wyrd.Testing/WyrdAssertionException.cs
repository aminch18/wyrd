namespace Wyrd.Testing;

/// <summary>A scenario expectation that did not hold. Any test framework reports it as a failure.</summary>
public sealed class WyrdAssertionException : Exception
{
    public WyrdAssertionException()
    {
    }

    public WyrdAssertionException(string message)
        : base(message)
    {
    }

    public WyrdAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
