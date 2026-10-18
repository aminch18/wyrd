namespace Shop.Orders;

/// <summary>A nonnegative decimal amount in one currency.</summary>
public sealed record Money
{
    public Money(decimal amount, string currency)
    {
        if (!IsValid(amount, currency))
        {
            ArgumentOutOfRangeException.ThrowIfNegative(amount);
            ArgumentNullException.ThrowIfNull(currency);
            throw new ArgumentException("Currency must contain three ASCII letters.", nameof(currency));
        }

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public static bool IsValid(decimal amount, string? currency) =>
        amount >= 0 && currency is { Length: 3 } && currency.All(char.IsAsciiLetter);

    public Money Add(Money other)
    {
        RequireSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        RequireSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    private void RequireSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency)
        {
            throw new ArgumentException("Money arithmetic requires matching currencies.", nameof(other));
        }
    }
}
