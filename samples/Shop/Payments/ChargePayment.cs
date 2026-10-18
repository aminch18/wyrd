using Wyrd;

namespace Shop.Payments;

public sealed record ChargePayment(string OrderId, decimal Amount) : ICommand;

/// <summary>Slice: charge the customer's card.</summary>
public sealed class ChargePaymentSlice : Slice<ChargePayment, Payment, PaymentCharged, PaymentError>
{
    private const decimal CardLimit = 1_000m;

    public override object KeyOf(ChargePayment command) => command.OrderId;

    public override Result<PaymentCharged, PaymentError> Decide(ChargePayment command, Payment payment) => payment switch
    {
        Payment.None when command.Amount > CardLimit => PaymentError.CardDeclined,
        Payment.None => new PaymentCharged(command.OrderId, command.Amount),
        Payment.Charged or Payment.Refunded => PaymentError.AlreadyCharged,
    };
}
