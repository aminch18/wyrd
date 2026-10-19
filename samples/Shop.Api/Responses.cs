using Shop.Orders;

namespace Shop.Api;

public sealed record OrderResponse(OrderSummary? Order, OrderDetail Details, string? Fulfillment);

public sealed record StockResponse(string Sku, int Available);

public sealed record ClockResponse(string Now);
