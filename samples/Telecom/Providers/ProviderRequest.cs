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

public sealed record ProviderReply(string? Receipt, ProviderError? Error = null);
