using Wyrd.Pipe;
using Wyrd.Processes;
using static Wyrd.Pipe.Pipeline;

namespace Wyrd.Tests;

public static class PipeProcess
{
    public static Process OrderFulfillment =>
        Start.On<OrderPlaced>(e => e.OrderId)
          >> Step(Stock.Reserve).Undo(Stock.Release)
          >> Step(Payment.Charge).Retry(3.Times, Backoff.Exponential).Undo(Payment.Refund)
          >> Await<PaymentConfirmed>(e => e.OrderId).Within(15.Minutes)
          >> Step(Shipping.Ship);
}
