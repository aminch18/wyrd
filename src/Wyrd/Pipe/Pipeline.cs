using System.Runtime.CompilerServices;
using Wyrd.Processes;

namespace Wyrd.Pipe;

/// <summary>
/// Opt-in pipe syntax. Import with <c>using Wyrd.Pipe;</c> and <c>using static Wyrd.Pipe.Pipeline;</c>.
/// It builds exactly the same <see cref="Process"/> as the fluent syntax.
/// Step functions are checked against the trigger when the process is built.
/// </summary>
public static class Pipeline
{
    public static Fragment Step(Delegate action) => new(new StepNode(StepRef.Untyped(action)));

    public static Fragment Await<TEvent>(Func<TEvent, object> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new(new AwaitNode(typeof(TEvent)) { KeyOf = e => key((TEvent)e) });
    }

    extension<TTrigger>(ProcessBuilder<TTrigger>)
    {
        public static ProcessBuilder<TTrigger> operator >>(ProcessBuilder<TTrigger> process, Fragment next)
        {
            ArgumentNullException.ThrowIfNull(process);
            ArgumentNullException.ThrowIfNull(next);
            return process.Append(next.Node);
        }
    }
}

public static class Start
{
    public static ProcessBuilder<TEvent> On<TEvent>(Func<TEvent, object> key, [CallerMemberName] string name = "") =>
        Process.StartOn(key, name);
}

/// <summary>A single pipe stage with its modifiers, e.g. <c>Step(Payment.Charge).Retry(3.Times)</c>.</summary>
public sealed class Fragment
{
    internal Fragment(ProcessNode node) => Node = node;

    internal ProcessNode Node { get; }

    public Fragment Undo(Delegate compensation) => new(NodeModifiers.Undo(Node, StepRef.Untyped(compensation)));

    public Fragment Retry(Attempts attempts, Backoff backoff = Backoff.Exponential) =>
        new(NodeModifiers.Retry(Node, attempts, backoff));

    public Fragment Within(TimeSpan timeout) => new(NodeModifiers.Within(Node, timeout));
}
