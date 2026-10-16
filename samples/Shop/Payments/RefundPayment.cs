using Wyrd;

namespace Shop.Payments;

/// <summary>Slice: refund a charge.</summary>
public sealed record RefundPayment(string OrderId) : ICommand<RefundPayment, Payment, PaymentRefunded, PaymentError>
{
    public object Key => OrderId;

    public static Result<PaymentRefunded, PaymentError> Decide(RefundPayment cmd, Payment payment) => payment switch
    {
        Payment.Charged charged => new PaymentRefunded(cmd.OrderId, charged.Amount),
        _ => PaymentError.NothingToRefund,
    };
}
