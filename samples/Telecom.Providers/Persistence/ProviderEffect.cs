namespace Telecom.Providers.Persistence;

public sealed class ProviderEffect
{
    public string Key { get; set; } = "";
    public string Request { get; set; } = "";
    public string Receipt { get; set; } = "";
    public string? Resource { get; set; }
    public Guid DeliveryKey { get; set; }
}
