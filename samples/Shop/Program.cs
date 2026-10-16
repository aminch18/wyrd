using Shop.Fulfillment;
using Shop.Inventory;
using Shop.Orders;
using Shop.Payments;
using Wyrd;
using Wyrd.Runtime;

if (args.Contains("--diagram"))
{
    Console.WriteLine(Processes.OrderFulfillment.ToMermaid());
    return;
}

var shop = new InMemoryRuntime(Processes.OrderFulfillment);
var printed = 0;

shop.Send(new Restock("KEYBOARD", 5));
Flush("The warehouse receives 5 keyboards");

shop.Send(new PlaceOrder("A-1001", "KEYBOARD", 1, 89m));
shop.Publish(new PaymentConfirmed("A-1001"));
Flush("1. Happy path: order placed, payment confirmed, shipped");

shop.FailNext<ChargePayment>(times: 2);
shop.Send(new PlaceOrder("A-1002", "KEYBOARD", 1, 89m));
shop.Publish(new PaymentConfirmed("A-1002"));
Flush("2. The payment gateway is flaky: transient failures are retried");

shop.Send(new PlaceOrder("A-1003", "KEYBOARD", 2, 2_500m));
Flush("3. Card declined: the reserved stock is released");

shop.Send(new PlaceOrder("A-1004", "KEYBOARD", 50, 89m));
Flush("4. Out of stock: nothing completed, nothing to compensate");

shop.Send(new PlaceOrder("A-1005", "KEYBOARD", 1, 89m));
shop.Advance(15.Minutes);
Flush("5. The customer abandons 3-D Secure: timeout, refund, release");

shop.Publish(new OrderPlaced("A-1001", "KEYBOARD", 1, 89m));
Flush("6. The broker redelivers a message: duplicates are ignored");

var keyboards = shop.StateOf<StockItem>("KEYBOARD").Available;
Console.WriteLine();
Write(ConsoleColor.White, $"Keyboards in stock: {keyboards} (5 received, 2 shipped, every failed order released its units)");
Console.WriteLine();
Write(ConsoleColor.DarkGray, "Run with --diagram to print the process as a Mermaid diagram.");

void Flush(string title)
{
    Console.WriteLine();
    Write(ConsoleColor.White, title);
    foreach (var entry in shop.Journal.Skip(printed))
    {
        Write(ColorOf(entry.Message), $"  {entry}");
    }

    printed = shop.Journal.Count;
}

static ConsoleColor ColorOf(string message) => message switch
{
    _ when message.StartsWith("ok", StringComparison.Ordinal) => ConsoleColor.Green,
    _ when message.StartsWith("completed", StringComparison.Ordinal) => ConsoleColor.Green,
    _ when message.StartsWith("rejected", StringComparison.Ordinal) => ConsoleColor.Red,
    _ when message.StartsWith("FAILED", StringComparison.Ordinal) => ConsoleColor.Red,
    _ when message.StartsWith("transient", StringComparison.Ordinal) => ConsoleColor.Yellow,
    _ when message.StartsWith("timeout", StringComparison.Ordinal) => ConsoleColor.Yellow,
    _ when message.StartsWith("compensat", StringComparison.Ordinal) => ConsoleColor.Magenta,
    _ when message.StartsWith("awaiting", StringComparison.Ordinal) => ConsoleColor.Cyan,
    _ when message.StartsWith("duplicate", StringComparison.Ordinal) => ConsoleColor.DarkYellow,
    _ => ConsoleColor.Gray,
};

static void Write(ConsoleColor color, string text)
{
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ResetColor();
}
