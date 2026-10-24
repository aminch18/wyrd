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
    /// <summary>A step: the trigger, then optionally events earlier steps produced, to a command.</summary>
    public static Fragment Step(Delegate action) => new(new StepNode(StepRef.Untyped(action)));

    /// <summary>The command sent once compensation has finished.</summary>
    public static Fragment OnCompensated(Delegate outcome) => new(new CompensatedNode(StepRef.Untyped(outcome)));

    /// <summary>Creates a wait stage correlated by the process key. Set its business deadline with <c>Within</c>.</summary>
    public static Fragment Await<TEvent>(Func<TEvent, object> key)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(key);
        return new(new AwaitNode(typeof(TEvent)) { KeyOf = e => key((TEvent)e) });
    }

    extension<TTrigger>(ProcessBuilder<TTrigger>)
        where TTrigger : IEvent
    {
        /// <summary>Appends a pipe stage, binding its step functions to the process trigger.</summary>
        /// <example><code>Start.On&lt;OrderPlaced&gt;(e =&gt; e.OrderId) &gt;&gt; Step(Stock.Reserve).Retry(3.Times)</code></example>
        public static ProcessBuilder<TTrigger> operator >>(ProcessBuilder<TTrigger> process, Fragment next)
        {
            ArgumentNullException.ThrowIfNull(process);
            ArgumentNullException.ThrowIfNull(next);
            return process.Append(next.Node);
        }
    }
}

/// <summary>The starting point for an opt-in pipe process definition.</summary>
public static class Start
{
    /// <summary>Starts a process on an event, correlated by its business key. The caller member name supplies the default process name.</summary>
    public static ProcessBuilder<TEvent> On<TEvent>(Func<TEvent, object> key, [CallerMemberName] string name = "")
        where TEvent : IEvent =>
        Process.StartOn(key, name);
}

/// <summary>A single pipe stage with its modifiers, e.g. <c>Step(Payment.Charge).Retry(3.Times)</c>.</summary>
public sealed class Fragment
{
    internal Fragment(ProcessNode node) => Node = node;

    internal ProcessNode Node { get; }

    /// <summary>How this step is undone; undoing always insists (10 retries by default, adjustable).</summary>
    public Fragment Undo(Delegate compensation, Attempts? insist = null, Backoff backoff = Backoff.Exponential) =>
        new(NodeModifiers.Undo(Node, StepRef.Untyped(compensation), insist is { } a ? new RetryPolicy(a, backoff) : null));

    /// <summary>When this step's retries run out, compensate automatically instead of suspending.</summary>
    public Fragment OrCompensate() => new(NodeModifiers.OrCompensate(Node));

    /// <summary>Declares that this step is not retried, and why.</summary>
    public Fragment NoRetry(string reason) => new(NodeModifiers.NoRetry(Node, reason));

    /// <summary>Declares that this step needs no undo if a later step fails, and why.</summary>
    public Fragment NoCompensation(string reason) => new(NodeModifiers.NoCompensation(Node, reason));

    /// <summary>Sets the retry count and backoff for this step or compensation outcome.</summary>
    public Fragment Retry(Attempts attempts, Backoff backoff = Backoff.Exponential) =>
        new(NodeModifiers.Retry(Node, attempts, backoff));

    /// <summary>Sets this wait stage's business deadline; expiry starts compensation.</summary>
    public Fragment Within(TimeSpan timeout) => new(NodeModifiers.Within(Node, timeout));
}
