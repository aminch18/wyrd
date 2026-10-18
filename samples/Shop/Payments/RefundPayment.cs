using Wyrd;

namespace Shop.Payments;

public sealed record RefundPayment(string OrderId) : ICommand;

/// <summary>Slice: refund a charge.</summary>
public sealed class RefundPaymentSlice : Slice<RefundPayment, Payment, PaymentRefunded, PaymentError>
{
    public override object KeyOf(RefundPayment command) => command.OrderId;

    public override Result<PaymentRefunded, PaymentError> Decide(RefundPayment command, Payment payment) => payment switch
    {
        Payment.Charged charged => new PaymentRefunded(command.OrderId, charged.Amount),
        Payment.None or Payment.Refunded => PaymentError.NothingToRefund,
    };
}
