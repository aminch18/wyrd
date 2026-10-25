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

        if (!HasCanonicalPhoneNumber())
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

    private bool HasCanonicalPhoneNumber()
    {
        if (Msisdn is not { Length: >= 9 and <= 16 })
        {
            return false;
        }

        if (Msisdn[0] != '+' || Msisdn[1] == '0')
        {
            return false;
        }

        foreach (var digit in Msisdn[1..])
        {
            if (!char.IsAsciiDigit(digit))
            {
                return false;
            }
        }

        return true;
    }
}
