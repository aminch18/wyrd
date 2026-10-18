namespace Wyrd.Analyzers.Tests;

public class ProcessRuleTests
{
    private const string Domain = """
        using Wyrd.Processes;
        using Wyrd.Pipe;
        using static Wyrd.Pipe.Pipeline;

        public sealed record OrderPlaced(string Id) : IDomainEvent;
        public sealed record PaymentConfirmed(string Id) : IDomainEvent;
        public sealed record ReserveStock(string Id) : ICommand;
        public sealed record ReleaseStock(string Id) : ICommand;
        public sealed record ChargePayment(string Id) : ICommand;
        public sealed record SendEmail(string Id) : ICommand;
        public sealed record ShipOrder(string Id) : ICommand;

        public static class Steps
        {
            public static ReserveStock Reserve(OrderPlaced e) => new(e.Id);
            public static ReleaseStock Release(OrderPlaced e) => new(e.Id);
            public static ChargePayment Charge(OrderPlaced e) => new(e.Id);
            public static SendEmail Email(OrderPlaced e) => new(e.Id);
            public static ShipOrder Ship(OrderPlaced e) => new(e.Id);
        }

        """;

    private static async Task<string[]> Rules(string process)
    {
        var diagnostics = await AnalyzerHarness.Analyze(Domain + "public static class Definitions\n{\n" + process + "\n}");
        return [.. diagnostics.Select(d => d.Id).Where(id => id.StartsWith("WYRD001", StringComparison.Ordinal))];
    }

    [Fact]
    public async Task a_robust_process_is_clean() => Assert.Empty(await Rules("""
        public static Process Fluent => Process.StartOn<OrderPlaced>(e => e.Id)
            .Then(Steps.Reserve).Retry(5.Times).Undo(Steps.Release)
            .Then(Steps.Email).NoCompensation("an email can't be unsent")
            .Await<PaymentConfirmed>(e => e.Id).Within(15.Minutes)
            .Then(Steps.Ship);

        public static Process Piped => Start.On<OrderPlaced>(e => e.Id, "Piped")
            >> Step(Steps.Reserve).Undo(Steps.Release).Retry(5.Times)
            >> Step(Steps.Email).NoCompensation("an email can't be unsent")
            >> Await<PaymentConfirmed>(e => e.Id).Within(15.Minutes)
            >> Step(Steps.Ship);
        """));

    [Fact]
    public async Task an_undo_without_retries_is_reported() =>
        Assert.Equal(["WYRD0010", "WYRD0010"], await Rules("""
            public static Process Fluent => Process.StartOn<OrderPlaced>(e => e.Id)
                .Then(Steps.Reserve).Undo(Steps.Release)
                .Then(Steps.Ship);

            public static Process Piped => Start.On<OrderPlaced>(e => e.Id, "Piped")
                >> Step(Steps.Reserve).Undo(Steps.Release)
                >> Step(Steps.Ship);
            """));

    [Fact]
    public async Task a_step_followed_by_others_must_say_how_it_is_undone() =>
        Assert.Equal(["WYRD0011", "WYRD0011"], await Rules("""
            public static Process Fluent => Process.StartOn<OrderPlaced>(e => e.Id)
                .Then(Steps.Charge).Retry(3.Times)
                .Then(Steps.Ship);

            public static Process Piped => Start.On<OrderPlaced>(e => e.Id, "Piped")
                >> Step(Steps.Charge)
                >> Step(Steps.Ship);
            """));

    [Fact]
    public async Task the_last_step_needs_no_undo_even_with_an_outcome() => Assert.Empty(await Rules("""
        public static Process Fluent => Process.StartOn<OrderPlaced>(e => e.Id)
            .Then(Steps.Ship)
            .OnCompensated(Steps.Email).Retry(3.Times);

        public static Process Piped => Start.On<OrderPlaced>(e => e.Id, "Piped")
            >> Step(Steps.Ship)
            >> OnCompensated(Steps.Email);
        """));

    [Fact]
    public async Task a_wait_without_timeout_is_reported() =>
        Assert.Equal(["WYRD0012", "WYRD0012"], await Rules("""
            public static Process Fluent => Process.StartOn<OrderPlaced>(e => e.Id)
                .Await<PaymentConfirmed>(e => e.Id)
                .Then(Steps.Ship);

            public static Process Piped => Start.On<OrderPlaced>(e => e.Id, "Piped")
                >> Await<PaymentConfirmed>(e => e.Id)
                >> Step(Steps.Ship);
            """));

    [Fact]
    public async Task the_message_names_the_step()
    {
        var diagnostics = await AnalyzerHarness.Analyze(Domain + """
            public static class Definitions
            {
                public static Process Fluent => Process.StartOn<OrderPlaced>(e => e.Id).Then(Steps.Reserve).Undo(Steps.Release);
            }
            """);

        var undo = Assert.Single(diagnostics, d => d.Id == "WYRD0010");
        Assert.StartsWith("Step 'Steps.Reserve' can be undone but has no .Retry(...)", undo.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
