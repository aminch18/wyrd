namespace Shop.Api;

public sealed record RestockRequest(int Quantity);

public sealed record PlaceOrderRequest(string OrderId, string Sku, int Quantity, decimal Amount);

public sealed record PaymentWebhook(string OrderId);
