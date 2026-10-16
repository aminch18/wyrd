using Wyrd.Pipe;
using Wyrd.Processes;
using static Wyrd.Pipe.Pipeline;

namespace Wyrd.Tests;

public static class PipeProcess
{
    public static Process OrderFulfillment =>
        Start.On<OrderPlaced>()
          >> Step(Stock.Reserve).Undo(Stock.Release)
          >> Step(Payment.Charge).Retry(3.Times, Backoff.Exponential).Undo(Payment.Refund)
          >> Await<PaymentConfirmed>().Within(15.Minutes)
          >> Step(Shipping.Ship);
}
