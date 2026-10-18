using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Wyrd;

namespace Shop.Orders;

public enum OrderError
{
    EmptyOrder,
    AlreadyPlaced,
    AlreadyCancelled,
    NotFound,
    DraftExists,
    InvalidLine,
    InvalidMoney,
    DuplicateLine,
    LineNotFound,
    LineAlreadyCancelled,
    TooManyLines,
    CurrencyMismatch,
    TotalTooLarge,
    QuantityTooLarge,
    MultipleSkusNotSupported,
    CurrencyNotSupported,
}

/// <summary>The consistency boundary: every order slice delegates its rules to this state.</summary>
[Union]
[JsonDerivedType(typeof(None), "None")]
[JsonDerivedType(typeof(Draft), "Draft")]
[JsonDerivedType(typeof(Placed), "Placed")]
[JsonDerivedType(typeof(Cancelled), "Cancelled")]
public abstract record Order : IState<Order>
{
    // Includes cancelled identities: a draft is bounded, and a removed line id is never reused.
    public const int MaxLines = 10;

    public static Order Initial { get; } = new None();

    public abstract ImmutableList<OrderLine> Lines { get; }

    public Money? Total => TotalOf(Lines);

    public Result<LineAdded, OrderError> AddLine(AddLine command)
    {
        if (EditError(allowNew: true) is { } error)
        {
            return error;
        }

        if (!Money.IsValid(command.Amount, command.Currency))
        {
            return OrderError.InvalidMoney;
        }

        var amount = new Money(command.Amount, command.Currency);
        if (!OrderLine.IsValid(command.LineId, command.Sku, command.Quantity, amount))
        {
            return OrderError.InvalidLine;
        }

        if (Lines.Any(line => line.LineId == command.LineId))
        {
            return OrderError.DuplicateLine;
        }

        if (Lines.Count >= MaxLines)
        {
            return OrderError.TooManyLines;
        }

        if (Total is { } total)
        {
            if (total.Currency != amount.Currency)
            {
                return OrderError.CurrencyMismatch;
            }

            try
            {
                _ = total.Add(amount);
            }
            catch (OverflowException)
            {
                return OrderError.TotalTooLarge;
            }
        }

        return new LineAdded(command.OrderId, new OrderLine(command.LineId, command.Sku, command.Quantity, amount));
    }

    public Result<LineCancelled, OrderError> CancelLine(CancelLine command)
    {
        if (EditError() is { } error)
        {
            return error;
        }

        var line = Lines.FirstOrDefault(item => item.LineId == command.LineId);
        if (line is null)
        {
            return OrderError.LineNotFound;
        }

        return line.IsCancelled ? OrderError.LineAlreadyCancelled : new LineCancelled(command.OrderId, line.LineId);
    }

    /// <summary>The original one-line checkout remains a shortcut, with no draft to replace accidentally.</summary>
    public Result<OrderPlaced, OrderError> Place(PlaceOrder command)
    {
        if (EditError(allowNew: true) is { } error)
        {
            return error;
        }

        if (this is Draft)
        {
            return OrderError.DraftExists;
        }

        if (command.Quantity <= 0)
        {
            return OrderError.EmptyOrder;
        }

        if (!Money.IsValid(command.Amount, "EUR"))
        {
            return OrderError.InvalidMoney;
        }

        return OrderLine.IsValid("initial", command.Sku, command.Quantity, new Money(command.Amount, "EUR"))
            ? new OrderPlaced(command.OrderId, command.Sku, command.Quantity, command.Amount)
            : OrderError.InvalidLine;
    }

    /// <summary>Freeze the draft into the snapshot consumed by the existing single-SKU, EUR fulfillment.</summary>
    public Result<OrderPlaced, OrderError> Submit(SubmitOrder command)
    {
        if (EditError() is { } error)
        {
            return error;
        }

        var active = Lines.Where(line => !line.IsCancelled).ToList();
        if (active.Count == 0)
        {
            return OrderError.EmptyOrder;
        }

        if (active.Any(line => line.Sku != active[0].Sku))
        {
            return OrderError.MultipleSkusNotSupported;
        }

        var total = Total!;
        if (total.Currency != "EUR")
        {
            return OrderError.CurrencyNotSupported;
        }

        int quantity;
        try
        {
            quantity = active.Sum(line => line.Quantity);
        }
        catch (OverflowException)
        {
            return OrderError.QuantityTooLarge;
        }

        return new OrderPlaced(command.OrderId, active[0].Sku, quantity, total.Amount, Lines);
    }

    /// <summary>User cancellation is limited to drafts; fulfillment uses Cancel after compensation.</summary>
    public Result<OrderCancelled, OrderError> CancelDraft(CancelDraftOrder command) =>
        EditError() is { } error ? error : new OrderCancelled(command.OrderId);

    public Result<OrderCancelled, OrderError> Cancel(CancelOrder command) =>
        UnavailableError() is { } error ? error : new OrderCancelled(command.OrderId);

    private OrderError? UnavailableError() => this switch
    {
        None => OrderError.NotFound,
        Cancelled => OrderError.AlreadyCancelled,
        Draft or Placed => null,
    };

    private OrderError? EditError(bool allowNew = false)
    {
        if (allowNew && this is None)
        {
            return null;
        }

        return UnavailableError() ?? (this is Placed ? OrderError.AlreadyPlaced : null);
    }

    public Order Evolve(IDomainEvent domainEvent) => (this, domainEvent) switch
    {
        (None, LineAdded added) => new Draft([added.Line]),
        (Draft, LineAdded added) => new Draft(Lines.Add(added.Line)),
        (Draft, LineCancelled cancelled) => new Draft(Lines.Select(line =>
            line.LineId == cancelled.LineId ? line.Cancel() : line).ToImmutableList()),
        (None or Draft, OrderPlaced placed) => new Placed(LinesOf(placed)),
        (Draft or Placed, OrderCancelled) => new Cancelled(Lines),
        _ => this,
    };

    public static ImmutableList<OrderLine> LinesOf(OrderPlaced placed) => placed.Lines
        ?? [new OrderLine("initial", placed.Sku, placed.Quantity, new Money(placed.Amount, "EUR"))];

    public static Money? TotalOf(ImmutableList<OrderLine> lines) => lines.Count == 0 ? null
        : lines.Where(line => !line.IsCancelled)
            .Aggregate(new Money(0, lines[0].Amount.Currency), (total, line) => total.Add(line.Amount));

    public sealed record None : Order
    {
        public override ImmutableList<OrderLine> Lines => ImmutableList<OrderLine>.Empty;
    }

    public sealed record Draft(ImmutableList<OrderLine> Lines) : Order
    {
        public override ImmutableList<OrderLine> Lines { get; } = Lines;
    }

    public sealed record Placed(ImmutableList<OrderLine> Lines) : Order
    {
        public override ImmutableList<OrderLine> Lines { get; } = Lines;
    }

    public sealed record Cancelled(ImmutableList<OrderLine> Lines) : Order
    {
        public override ImmutableList<OrderLine> Lines { get; } = Lines;
    }
}
