using System.Text.Json;
using Shop.Orders;

namespace Shop.Tests;

public class MoneySpecs
{
    [Theory]
    [InlineData(0, "eur", true)]
    [InlineData(10, "USD", true)]
    [InlineData(-1, "EUR", false)]
    [InlineData(1, null, false)]
    [InlineData(1, "", false)]
    [InlineData(1, "ÉUR", false)]
    [InlineData(1, "EURO", false)]
    public void money_can_be_validated_before_construction(decimal amount, string? currency, bool expected) =>
        Assert.Equal(expected, Money.IsValid(amount, currency));

    [Fact]
    public void money_compares_by_amount_and_normalized_currency() =>
        Assert.Equal(new Money(12.50m, "eur"), new Money(12.5m, "EUR"));

    [Fact]
    public void adding_money_keeps_decimal_precision() =>
        Assert.Equal(new Money(0.3m, "EUR"), new Money(0.1m, "EUR").Add(new Money(0.2m, "EUR")));

    [Fact]
    public void subtracting_money_can_leave_zero() =>
        Assert.Equal(new Money(0m, "EUR"), new Money(12m, "EUR").Subtract(new Money(12m, "EUR")));

    [Fact]
    public void subtracting_money_keeps_the_remaining_amount() =>
        Assert.Equal(new Money(7.5m, "EUR"), new Money(10m, "EUR").Subtract(new Money(2.5m, "EUR")));

    [Fact]
    public void arithmetic_refuses_mixed_currencies()
    {
        var euros = new Money(12m, "EUR");
        var dollars = new Money(1m, "USD");

        Assert.Throws<ArgumentException>(() => euros.Add(dollars));
        Assert.Throws<ArgumentException>(() => euros.Subtract(dollars));
    }

    [Fact]
    public void subtraction_cannot_make_money_negative() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(1m, "EUR").Subtract(new Money(2m, "EUR")));

    [Fact]
    public void money_cannot_start_negative() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-0.01m, "EUR"));

    [Theory]
    [InlineData("")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData(" EU")]
    [InlineData("ÉUR")]
    public void currency_requires_three_ascii_letters(string currency) =>
        Assert.Throws<ArgumentException>(() => new Money(1m, currency));

    [Fact]
    public void currency_cannot_be_null() =>
        Assert.Throws<ArgumentNullException>(() => new Money(1m, null!));

    [Fact]
    public void arithmetic_requires_an_amount()
    {
        var money = new Money(1m, "EUR");

        Assert.Throws<ArgumentNullException>(() => money.Add(null!));
        Assert.Throws<ArgumentNullException>(() => money.Subtract(null!));
    }

    [Fact]
    public void overflowing_an_amount_is_rejected() =>
        Assert.Throws<OverflowException>(() => new Money(decimal.MaxValue, "EUR").Add(new Money(1m, "EUR")));

    [Fact]
    public void money_survives_a_json_round_trip()
    {
        var money = new Money(123.45m, "eur");

        Assert.Equal(money, JsonSerializer.Deserialize<Money>(JsonSerializer.Serialize(money)));
    }

    [Theory]
    [InlineData("{\"Amount\":-1,\"Currency\":\"EUR\"}")]
    [InlineData("{\"Amount\":1,\"Currency\":\"\"}")]
    [InlineData("{\"Amount\":1}")]
    public void json_cannot_bypass_money_validation(string json) =>
        Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize<Money>(json));
}
