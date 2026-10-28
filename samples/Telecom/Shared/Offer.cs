namespace Telecom;

public sealed record Offer(
    string SubscriptionId,
    string CustomerId,
    string Msisdn,
    long Epoch,
    string ProfileId,
    decimal InitialAmount)
{
    public bool IsValid => Validate() is null;

    public RuleError? Validate()
    {
        if (!ValidId(SubscriptionId) || !ValidId(CustomerId) || !ValidId(ProfileId))
        {
            return RuleError.InvalidInput;
        }

        if (!ValidMsisdn(Msisdn))
        {
            return RuleError.InvalidInput;
        }

        if (Epoch <= 0 || InitialAmount <= 0)
        {
            return RuleError.InvalidInput;
        }

        return null;
    }

    // IDs are also URL segments and components of provider operation keys.
    public static bool ValidId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>An international number: '+', a non-zero first digit, 8 to 15 digits in all (E.164).</summary>
    public static bool ValidMsisdn(string? value)
    {
        if (value is not { Length: >= 9 and <= 16 })
        {
            return false;
        }

        if (value[0] != '+' || value[1] == '0')
        {
            return false;
        }

        foreach (var digit in value[1..])
        {
            if (!char.IsAsciiDigit(digit))
            {
                return false;
            }
        }

        return true;
    }
}
