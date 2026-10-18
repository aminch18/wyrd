namespace Shop.Orders;

/// <summary>An identified order line; Amount is the total for its whole quantity.</summary>
public sealed record OrderLine
{
    public OrderLine(string lineId, string sku, int quantity, Money amount, bool isCancelled = false)
    {
        if (!IsValid(lineId, sku, quantity, amount))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
            ArgumentException.ThrowIfNullOrWhiteSpace(sku);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
            ArgumentNullException.ThrowIfNull(amount);
        }

        LineId = lineId;
        Sku = sku;
        Quantity = quantity;
        Amount = amount;
        IsCancelled = isCancelled;
    }

    public string LineId { get; }
    public string Sku { get; }
    public int Quantity { get; }
    public Money Amount { get; }
    public bool IsCancelled { get; }

    public static bool IsValid(string? lineId, string? sku, int quantity, Money? amount) =>
        !string.IsNullOrWhiteSpace(lineId) && !string.IsNullOrWhiteSpace(sku) && quantity > 0 && amount is not null;

    public OrderLine Cancel() => new(LineId, Sku, Quantity, Amount, isCancelled: true);
}
