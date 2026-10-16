using Wyrd.Processes;

namespace Wyrd.Pipe;

/// <summary>
/// Opt-in pipe syntax. Import with <c>using Wyrd.Pipe;</c> and <c>using static Wyrd.Pipe.Pipeline;</c>.
/// It builds exactly the same <see cref="Process"/> as the fluent syntax.
/// </summary>
public static class Pipeline
{
    public static Fragment Step(Delegate action) => new(new StepNode(StepRef.From(action)));

    public static Fragment Await<TEvent>() => new(new AwaitNode(typeof(TEvent)));

    extension(ProcessBuilder)
    {
        public static ProcessBuilder operator >>(ProcessBuilder process, Fragment next)
        {
            ArgumentNullException.ThrowIfNull(process);
            ArgumentNullException.ThrowIfNull(next);
            return process.Append(next.Node);
        }
    }
}

public static class Start
{
    public static ProcessBuilder On<TEvent>() => Process.StartOn<TEvent>();
}

/// <summary>A single pipe stage with its modifiers, e.g. <c>Step(Payment.Charge).Retry(3.Times)</c>.</summary>
public sealed class Fragment
{
    internal Fragment(ProcessNode node) => Node = node;

    internal ProcessNode Node { get; }

    public Fragment Undo(Delegate compensation) => new(NodeModifiers.Undo(Node, compensation));

    public Fragment Retry(Attempts attempts, Backoff backoff = Backoff.Exponential) =>
        new(NodeModifiers.Retry(Node, attempts, backoff));

    public Fragment Within(TimeSpan timeout) => new(NodeModifiers.Within(Node, timeout));
}
