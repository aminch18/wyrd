using Wyrd.Processes;

namespace Wyrd.Runtime;

/// <summary>
/// What application code (endpoints, handlers, jobs) needs from Wyrd, whatever the host: send commands, deliver
/// facts from other systems and read what happened. Depend on this, and switch hosts by configuration.
/// </summary>
public interface IWyrdRuntime
{
    /// <summary>Executes a command coming from outside a process and returns the decision.</summary>
    Task<Decision> SendAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>Delivers a fact from another context (a webhook, a message from another service...).</summary>
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers an integration event another service published (from a transport). Its message id is kept, so a
    /// message delivered twice is handled once.
    /// </summary>
    Task ReceiveAsync(TransportMessage message, CancellationToken cancellationToken = default);

    /// <summary>Where the instance of the process with this key stands, or null if there is no such instance.</summary>
    Task<ProcessStatus?> StatusOfAsync(Process process, object key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a suspended instance (after fixing what made it stop): what it was doing is sent again with the same
    /// message id, so a command that did run is not run twice.
    /// </summary>
    Task ResumeAsync(Process process, object key, CancellationToken cancellationToken = default);

    /// <summary>Compensates a suspended instance that was going forward; the step it gave up on is fenced.</summary>
    Task CompensateAsync(Process process, object key, CancellationToken cancellationToken = default);

    /// <summary>What the instance received and sent (Urd), or null if there is no such instance.</summary>
    Task<InstanceHistory?> HistoryOfAsync(Process process, object key, CancellationToken cancellationToken = default);

    /// <summary>The current state of the aggregate with this key (its initial state if nothing happened to it yet).</summary>
    Task<TState> StateOfAsync<TState>(object key, CancellationToken cancellationToken = default)
        where TState : IState<TState>;

    /// <summary>The read model with this key, or null if no event has built it yet.</summary>
    Task<TView?> ViewAsync<TView>(object key, CancellationToken cancellationToken = default)
        where TView : class;

    /// <summary>Every read model of this type, in a stable order.</summary>
    Task<IReadOnlyList<TView>> ViewsAsync<TView>(CancellationToken cancellationToken = default)
        where TView : class;
}
