namespace Telecom;

public enum ProviderError
{
    Declined,
    NotFound,
    Conflict,
}

public enum ProviderAction
{
    Capture,
    FindCapture,
    SubmitPort,
    CancelPort,
    Provision,
}

public sealed record ProviderRequest(ProviderAction Action, string LogicalKey, string Resource, string Owner, decimal Amount, int Attempt);

public sealed record ProviderReply(string? Receipt, ProviderError? Error = null)
{
    public string RequireReceipt()
    {
        if (string.IsNullOrWhiteSpace(Receipt))
        {
            throw new IOException("Provider returned neither a receipt nor an error.");
        }

        return Receipt;
    }
}
