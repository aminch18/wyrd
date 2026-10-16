using Wyrd;

namespace Shop.Payments;

/// <summary>Slice: charge the customer's card.</summary>
public sealed record ChargePayment(string OrderId, decimal Amount)
    : ICommand<ChargePayment, Payment, PaymentCharged, PaymentError>
{
    private const decimal CardLimit = 1_000m;

    public object Key => OrderId;

    public static Result<PaymentCharged, PaymentError> Decide(ChargePayment cmd, Payment payment) => payment switch
    {
        Payment.None when cmd.Amount > CardLimit => PaymentError.CardDeclined,
        Payment.None => new PaymentCharged(cmd.OrderId, cmd.Amount),
        _ => PaymentError.AlreadyCharged,
    };
}
