using Shop.Payments;
using Wyrd.Testing;

namespace Shop.Tests;

public class PaymentsSpecs
{
    [Fact]
    public void a_first_charge_within_the_card_limit_is_accepted() =>
        Scenario.For(new ChargePaymentSlice())
            .When(new ChargePayment("A-1", 89m))
            .Then(new PaymentCharged("A-1", 89m))
            .ThenState(new Payment.Charged(89m));

    [Fact]
    public void a_charge_over_the_card_limit_is_declined() =>
        Scenario.For(new ChargePaymentSlice())
            .When(new ChargePayment("A-1", 2_500m))
            .ThenRejected(PaymentError.CardDeclined);

    [Fact]
    public void an_order_is_never_charged_twice() =>
        Scenario.For(new ChargePaymentSlice())
            .Given(new PaymentCharged("A-1", 89m))
            .When(new ChargePayment("A-1", 89m))
            .ThenRejected(PaymentError.AlreadyCharged);

    [Fact]
    public void a_refund_returns_what_was_charged() =>
        Scenario.For(new RefundPaymentSlice())
            .Given(new PaymentCharged("A-1", 89m))
            .When(new RefundPayment("A-1"))
            .Then(new PaymentRefunded("A-1", 89m))
            .ThenState(new Payment.Refunded());

    [Fact]
    public void nothing_is_refunded_if_nothing_was_charged() =>
        Scenario.For(new RefundPaymentSlice())
            .When(new RefundPayment("A-1"))
            .ThenRejected(PaymentError.NothingToRefund);
}
