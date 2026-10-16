using Shop;
using Shop.Contracts;
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

var shop = ShopModule.CreateRuntime();
var printed = 0;

await shop.SendAsync(new Restock("KEYBOARD", 5));
Flush("The warehouse receives 5 keyboards");

await shop.SendAsync(new PlaceOrder("A-1001", "KEYBOARD", 1, 89m));
await shop.PublishAsync(new PaymentConfirmed("A-1001"));
Flush("1. Happy path: order placed, payment confirmed, shipped");

shop.FailNext<ChargePayment>(times: 2);
await shop.SendAsync(new PlaceOrder("A-1002", "KEYBOARD", 1, 89m));
await shop.PublishAsync(new PaymentConfirmed("A-1002"));
await shop.AdvanceAsync(3.Seconds);
Flush("2. The payment gateway is flaky: retries with backoff; the early confirmation is parked, then used");

await shop.SendAsync(new PlaceOrder("A-1003", "KEYBOARD", 2, 2_500m));
Flush("3. Card declined: the reserved stock is released");

await shop.SendAsync(new PlaceOrder("A-1004", "KEYBOARD", 50, 89m));
Flush("4. Out of stock: nothing completed, nothing to compensate");

await shop.SendAsync(new PlaceOrder("A-1005", "KEYBOARD", 1, 89m));
await shop.AdvanceAsync(15.Minutes);
Flush("5. The customer abandons 3-D Secure: timeout, refund, release");

await shop.SendAsync(new PlaceOrder("A-1001", "KEYBOARD", 1, 89m));
Flush("6. The customer double-clicks \"Buy\": the second order is rejected");

var keyboards = shop.StateOf<StockItem>("KEYBOARD").Available;
Console.WriteLine();
Write(ConsoleColor.White, $"Keyboards in stock: {keyboards} (5 received, 2 shipped, every failed order released its units)");
Console.WriteLine();
Write(ConsoleColor.White, "Integration events published to other services (public contracts):");
foreach (var published in shop.Published)
{
    Write(ConsoleColor.Gray, $"  {published}");
}
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
    _ when message.StartsWith("published", StringComparison.Ordinal) => ConsoleColor.Blue,
    _ when message.Contains("parked", StringComparison.Ordinal) => ConsoleColor.DarkCyan,
    _ => ConsoleColor.Gray,
};

static void Write(ConsoleColor color, string text)
{
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ResetColor();
}
