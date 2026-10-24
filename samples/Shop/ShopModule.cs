using Shop.Fulfillment;
using Shop.Inventory;
using Shop.Orders;
using Shop.Payments;
using Shop.Shipping;
using Wyrd.Runtime;

namespace Shop;

/// <summary>Wires every slice, publication, projection and process of the shop into any Wyrd host.</summary>
public static class ShopModule
{
    /// <summary>The shop in memory, with source-generated JSON: tests, simulation and development.</summary>
    public static InMemoryRuntime CreateRuntime() => Register(new InMemoryRuntime(ShopJson.Default));

    /// <summary>The same wiring for any host: in memory or durable.</summary>
    public static TRuntime Register<TRuntime>(TRuntime runtime)
        where TRuntime : IWyrdBuilder<TRuntime> => runtime
        .AddSlice(new PlaceOrderSlice())
        .AddSlice(new AddLineSlice())
        .AddSlice(new CancelLineSlice())
        .AddSlice(new SubmitOrderSlice())
        .AddSlice(new CancelDraftOrderSlice())
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
        .AddProjection(new OrderDetails())
        .AddProcess(Processes.OrderFulfillment);
}
