using System.Globalization;
using Shop.Contracts;
using Shop.Fulfillment;
using Shop.Inventory;
using Shop.Orders;
using Wyrd;
using Wyrd.Runtime;

namespace Shop.Tests;

public class StockConsumptionSpecs
{
    [Fact]
    public async Task a_busy_sku_keeps_only_reservations_still_in_flight()
    {
        using var shop = ShopModule.CreateRuntime();
        const int orders = 200;
        await shop.SendAsync(new Restock("KEYBOARD", orders + 2));
        await shop.SendAsync(new PlaceOrder("waiting", "KEYBOARD", 1, 10m));

        for (var i = 0; i < orders; i++)
        {
            var id = i.ToString(CultureInfo.InvariantCulture);
            await shop.SendAsync(new PlaceOrder(id, "KEYBOARD", 1, 10m));
            await shop.PublishAsync(new PaymentConfirmed(id));
            Assert.Equal(ProcessStatus.Completed, shop.StatusOf(Processes.StockConsumption, id));
            Assert.Equal("waiting", Assert.Single(shop.StateOf<StockItem>("KEYBOARD").Reservations).Key);
        }

        Assert.Equal(1, shop.StateOf<StockItem>("KEYBOARD").Available);
        await shop.AdvanceAsync(15.Minutes);
        Assert.Empty(shop.StateOf<StockItem>("KEYBOARD").Reservations);
        Assert.Equal(2, shop.StateOf<StockItem>("KEYBOARD").Available);
    }

    [Fact]
    public async Task suspended_inventory_cleanup_can_resume_without_unwinding_shipping()
    {
        using var shop = ShopModule.CreateRuntime();
        await shop.SendAsync(new Restock("KEYBOARD", 5));
        await shop.SendAsync(new PlaceOrder("A-1", "KEYBOARD", 2, 10m));
        shop.FailNext<ConsumeStock>(times: 6);
        await shop.PublishAsync(new PaymentConfirmed("A-1"));
        await shop.AdvanceAsync(1.Minutes);

        Assert.Equal(ProcessStatus.Completed, shop.StatusOf(Processes.OrderFulfillment, "A-1"));
        Assert.Equal(ProcessStatus.Suspended, shop.StatusOf(Processes.StockConsumption, "A-1"));
        Assert.True(shop.StateOf<StockItem>("KEYBOARD").HasReserved("A-1"));
        Assert.Equal("Shipped", shop.View<OrderSummary>("A-1")!.Status);

        await shop.ResumeAsync(Processes.StockConsumption, "A-1");
        Assert.Equal(ProcessStatus.Completed, shop.StatusOf(Processes.StockConsumption, "A-1"));
        Assert.Empty(shop.StateOf<StockItem>("KEYBOARD").Reservations);
        Assert.Equal(3, shop.StateOf<StockItem>("KEYBOARD").Available);
        Assert.DoesNotContain(shop.History, e => e is StockReleased or OrderCancelled);
    }
}
