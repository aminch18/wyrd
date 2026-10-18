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

    Task<ProcessStatus?> StatusOfAsync(Process process, object key, CancellationToken cancellationToken = default);

    /// <summary>What the instance received and sent (Urd), or null if there is no such instance.</summary>
    Task<InstanceHistory?> HistoryOfAsync(Process process, object key, CancellationToken cancellationToken = default);

    Task<TState> StateOfAsync<TState>(object key, CancellationToken cancellationToken = default)
        where TState : IState<TState>;

    Task<TView?> ViewAsync<TView>(object key, CancellationToken cancellationToken = default)
        where TView : class;

    Task<IReadOnlyList<TView>> ViewsAsync<TView>(CancellationToken cancellationToken = default)
        where TView : class;
}
