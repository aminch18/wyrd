using System.Text.Json;
using Shop.Orders;

namespace Shop.Tests;

public class OrderLineSpecs
{
    [Fact]
    public void cancelling_a_line_preserves_its_identity_and_values()
    {
        var line = new OrderLine("line-1", "KEYBOARD", 3, new Money(10m, "EUR"));
        var cancelled = line.Cancel();

        Assert.False(line.IsCancelled);
        Assert.True(cancelled.IsCancelled);
        Assert.Equal(line.LineId, cancelled.LineId);
        Assert.Equal(line.Sku, cancelled.Sku);
        Assert.Equal(line.Quantity, cancelled.Quantity);
        Assert.Equal(line.Amount, cancelled.Amount);
        Assert.Equal(cancelled, cancelled.Cancel());
    }

    [Theory]
    [InlineData("line-1", "KEYBOARD", 1, true)]
    [InlineData(null, "KEYBOARD", 1, false)]
    [InlineData(" ", "KEYBOARD", 1, false)]
    [InlineData("line-1", null, 1, false)]
    [InlineData("line-1", " ", 1, false)]
    [InlineData("line-1", "KEYBOARD", 0, false)]
    [InlineData("line-1", "KEYBOARD", -1, false)]
    public void lines_can_be_validated_before_construction(string? lineId, string? sku, int quantity, bool expected) =>
        Assert.Equal(expected, OrderLine.IsValid(lineId, sku, quantity, new Money(10m, "EUR")));

    [Fact]
    public void line_validation_rejects_a_missing_amount() =>
        Assert.False(OrderLine.IsValid("line-1", "KEYBOARD", 1, null));

    [Fact]
    public void lines_compare_by_identity_and_values() =>
        Assert.Equal(new OrderLine("line-1", "KEYBOARD", 2, new Money(89m, "EUR")),
            new OrderLine("line-1", "KEYBOARD", 2, new Money(89m, "eur")));

    [Fact]
    public void two_lines_of_the_same_sku_keep_distinct_identities() =>
        Assert.NotEqual(new OrderLine("line-1", "KEYBOARD", 1, new Money(89m, "EUR")),
            new OrderLine("line-2", "KEYBOARD", 1, new Money(89m, "EUR")));

    [Fact]
    public void amount_is_the_total_for_the_whole_line()
    {
        var line = new OrderLine("line-1", "KEYBOARD", 3, new Money(10m, "EUR"));

        Assert.Equal(new Money(10m, "EUR"), line.Amount);
        Assert.Equal(3, line.Quantity);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void a_line_requires_a_nonblank_identity(string lineId) =>
        Assert.Throws<ArgumentException>(() => new OrderLine(lineId, "KEYBOARD", 1, new Money(89m, "EUR")));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void a_line_requires_a_nonblank_sku(string sku) =>
        Assert.Throws<ArgumentException>(() => new OrderLine("line-1", sku, 1, new Money(89m, "EUR")));

    [Fact]
    public void a_line_requires_an_identity_and_sku()
    {
        Assert.Throws<ArgumentNullException>(() => new OrderLine(null!, "KEYBOARD", 1, new Money(89m, "EUR")));
        Assert.Throws<ArgumentNullException>(() => new OrderLine("line-1", null!, 1, new Money(89m, "EUR")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void a_line_requires_a_positive_quantity(int quantity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OrderLine("line-1", "KEYBOARD", quantity, new Money(89m, "EUR")));

    [Fact]
    public void a_line_requires_an_amount() =>
        Assert.Throws<ArgumentNullException>(() => new OrderLine("line-1", "KEYBOARD", 1, null!));

    [Fact]
    public void a_line_survives_a_json_round_trip()
    {
        var line = new OrderLine("line-1", "KEYBOARD", 3, new Money(10m, "EUR"));

        Assert.Equal(line, JsonSerializer.Deserialize<OrderLine>(JsonSerializer.Serialize(line)));
    }

    [Fact]
    public void a_cancelled_line_survives_a_json_round_trip()
    {
        var line = new OrderLine("line-1", "KEYBOARD", 3, new Money(10m, "EUR")).Cancel();

        Assert.Equal(line, JsonSerializer.Deserialize<OrderLine>(JsonSerializer.Serialize(line)));
    }

    [Theory]
    [InlineData("{\"LineId\":\"\",\"Sku\":\"KEYBOARD\",\"Quantity\":1,\"Amount\":{\"Amount\":89,\"Currency\":\"EUR\"}}")]
    [InlineData("{\"LineId\":\"line-1\",\"Sku\":\"KEYBOARD\",\"Quantity\":0,\"Amount\":{\"Amount\":89,\"Currency\":\"EUR\"}}")]
    [InlineData("{\"LineId\":\"line-1\",\"Sku\":\"KEYBOARD\",\"Quantity\":1}")]
    public void json_cannot_bypass_line_validation(string json) =>
        Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize<OrderLine>(json));
}
