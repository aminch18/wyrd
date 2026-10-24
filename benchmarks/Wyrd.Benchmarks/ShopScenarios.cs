using System.Diagnostics;
using System.Globalization;
using Shop;
using Shop.Contracts;
using ShopProcesses = Shop.Fulfillment.Processes;
using Shop.Inventory;
using Shop.Orders;
using Wyrd.Runtime;

namespace Wyrd.Benchmarks;

public enum ShopScenario
{
    SingleSku,
    SkuPerOrder,
    Compensated,
    Timers,
}

internal static class ShopScenarios
{
    public static async Task RunAsync(ShopScenario scenario, int orders)
    {
        using var shop = ShopModule.CreateRuntime();
        if (scenario == ShopScenario.SingleSku)
        {
            await shop.SendAsync(new Restock("SKU", orders)).ConfigureAwait(false);
        }

        for (var index = 0; index < orders; index++)
        {
            var id = index.ToString(CultureInfo.InvariantCulture);
            var sku = scenario == ShopScenario.SingleSku ? "SKU" : "SKU-" + id;
            if (scenario != ShopScenario.SingleSku)
            {
                await shop.SendAsync(new Restock(sku, 1)).ConfigureAwait(false);
            }

            await shop.SendAsync(new PlaceOrder(id, sku, 1, scenario == ShopScenario.Compensated ? 2_500m : 10m)).ConfigureAwait(false);
            if (scenario is ShopScenario.SingleSku or ShopScenario.SkuPerOrder || scenario == ShopScenario.Timers && index % 2 == 0)
            {
                await shop.PublishAsync(new PaymentConfirmed(id)).ConfigureAwait(false);
            }
        }

        if (scenario == ShopScenario.Timers)
        {
            await shop.AdvanceAsync(TimeSpan.FromMinutes(16)).ConfigureAwait(false);
        }

        // Validate the workload, outside the command loop: a broken benchmark must not report cheap failed orders.
        for (var index = 0; index < orders; index++)
        {
            var id = index.ToString(CultureInfo.InvariantCulture);
            var expected = scenario == ShopScenario.Compensated || scenario == ShopScenario.Timers && index % 2 != 0
                ? ProcessStatus.Compensated : ProcessStatus.Completed;
            if (shop.StatusOf(ShopProcesses.OrderFulfillment, id) != expected)
            {
                throw new InvalidOperationException($"{scenario} order {id} did not reach {expected}.");
            }
        }
    }

    // Tracing counts every handled envelope in a separate probe, never in an allocation or timing measurement.
    // Two probe sizes capture both per-order work and fixed setup without hard-coding the Shop's process structure.
    public static async Task<MessageShape> CountMessagesAsync(ShopScenario scenario)
    {
        long messages = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Wyrd",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (activity.Kind == ActivityKind.Consumer)
                {
                    Interlocked.Increment(ref messages);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        await RunAsync(scenario, 2).ConfigureAwait(false);
        var two = messages;
        messages = 0;
        await RunAsync(scenario, 4).ConfigureAwait(false);
        var four = messages;
        var perOrder = (four - two) / 2d;
        var setup = two - 2 * perOrder;
        if (perOrder <= 0 || setup < 0)
        {
            throw new InvalidOperationException($"{scenario}: could not count handled envelopes.");
        }

        return new MessageShape(setup, perOrder);
    }
}

internal sealed record MessageShape(double Setup, double PerOrder)
{
    public double Total(int orders) => Setup + PerOrder * orders;
}
