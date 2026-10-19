namespace Wyrd.Tests;

public class ExceptionTests
{
    [Fact]
    public void failures_to_handle_are_wyrd_exceptions_and_still_invalid_operations()
    {
        Exception[] failures =
        [
            new ConcurrencyConflictException("conflict"),
            new UnknownMessageTypeException("shop.unknown v1"),
            new MissingJsonMetadataException(typeof(ExceptionTests), new NotSupportedException()),
            new MissingUpcasterException("no upcaster"),
            new NotAnIntegrationEventException("Shop.OrderPlaced"),
            new InstanceStateException("Fulfillment", "A-1", "not suspended"),
        ];

        Assert.All(failures, failure => Assert.IsAssignableFrom<WyrdException>(failure));
        Assert.All(failures, failure => Assert.IsAssignableFrom<InvalidOperationException>(failure));
    }

    [Fact]
    public async Task an_unknown_message_says_which_type()
    {
        using var runtime = new Runtime.InMemoryRuntime();
        var message = new Runtime.TransportMessage(Guid.NewGuid(), "shop.unknown v1", "{}");

        await runtime.ReceiveAsync(message); // a contract nobody uses here is ignored, not an error

        var ex = Assert.Throws<UnknownMessageTypeException>(() => Runtime.MessageTypes.Reflection().TypeOf("shop.unknown v1"));
        Assert.Equal("shop.unknown v1", ex.MessageType);
    }
}
