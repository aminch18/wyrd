using Shop.Fulfillment;
using Shop.Inventory;
using Shop.Orders;
using Shop.Payments;
using Shop.Shipping;
using Wyrd.Runtime;

namespace Shop;

/// <summary>Wires every slice, publication, projection and process of the shop, with source-generated JSON.</summary>
public static class ShopModule
{
    public static InMemoryRuntime CreateRuntime() => new InMemoryRuntime(ShopJson.Default)
        .AddSlice(new PlaceOrderSlice())
        .AddSlice(new CancelOrderSlice())
        .AddSlice(new RestockSlice())
        .AddSlice(new ReserveStockSlice())
        .AddSlice(new ReleaseStockSlice())
        .AddSlice(new ChargePaymentSlice())
        .AddSlice(new RefundPaymentSlice())
        .AddSlice(new ShipOrderSlice())
        .AddPublication(new PaymentChargedPublication())
        .AddPublication(new OrderShippedPublication())
        .AddProjection(new OrderSummaries())
        .AddProcess(Processes.OrderFulfillment);
}
