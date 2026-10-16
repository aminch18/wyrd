namespace Wyrd.Tests;

public class DeciderTests
{
    private static readonly Guid Id = Guid.NewGuid();

    [Fact]
    public void places_an_order_from_nothing() =>
        Assert.Equal(
            (Result<OrderPlaced, OrderError>)new OrderPlaced(Id.ToString(), 2, 0),
            PlaceOrder.Decide(new(Id, Lines: 2), new Order.None()));

    [Fact]
    public void rejects_an_empty_order() =>
        Assert.Equal(
            (Result<OrderPlaced, OrderError>)OrderError.EmptyOrder,
            PlaceOrder.Decide(new(Id, Lines: 0), new Order.None()));

    [Fact]
    public void rejects_placing_an_order_twice()
    {
        var result = PlaceOrder.Decide(new(Id, Lines: 1), new Order.Placed(Id, 1));

        Assert.Equal("already exists", result.Match(_ => "placed", e => e == OrderError.AlreadyExists ? "already exists" : "other"));
    }
}
