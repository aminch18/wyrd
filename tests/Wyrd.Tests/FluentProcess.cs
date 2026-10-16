using Wyrd.Processes;

namespace Wyrd.Tests;

public static class FluentProcess
{
    public static Process OrderFulfillment => Process
        .StartOn<OrderPlaced>(e => e.OrderId)
        .Then(Stock.Reserve).Undo(Stock.Release)
        .Then(Payment.Charge).Retry(3.Times, Backoff.Exponential).Undo(Payment.Refund)
        .Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
        .Then(Shipping.Ship);
}
