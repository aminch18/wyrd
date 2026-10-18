using Wyrd.Processes;

namespace Wyrd.Runtime;

/// <summary>
/// What every Wyrd host accepts. Write your module's wiring once against it and register the same slices,
/// publications, projections and processes in memory (tests, development) or on a durable host (production).
/// </summary>
/// <example>
/// <code>
/// public static TRuntime Register&lt;TRuntime&gt;(TRuntime runtime) where TRuntime : IWyrdBuilder&lt;TRuntime&gt; =>
///     runtime.AddSlice(new ChargePaymentSlice()).AddProcess(Processes.OrderFulfillment);
/// </code>
/// </example>
public interface IWyrdBuilder<out TSelf>
    where TSelf : IWyrdBuilder<TSelf>
{
    /// <summary>Registers the single owner of a command type.</summary>
    TSelf AddSlice<TCommand, TState, TEvent, TError>(Slice<TCommand, TState, TEvent, TError> slice)
        where TCommand : ICommand
        where TState : IState<TState>
        where TEvent : IDomainEvent
        where TError : notnull;

    /// <summary>Registers the integration that owns a command carried out by another system (the only I/O).</summary>
    TSelf AddIntegration<TCommand, TEvent, TError>(Integration<TCommand, TEvent, TError> integration)
        where TCommand : ICommand
        where TEvent : IDomainEvent
        where TError : notnull;

    /// <summary>Registers a mapping from a domain event to an integration event.</summary>
    TSelf AddPublication<TDomainEvent, TIntegrationEvent>(Publication<TDomainEvent, TIntegrationEvent> publication)
        where TDomainEvent : IDomainEvent
        where TIntegrationEvent : IIntegrationEvent;

    /// <summary>Registers an upcaster from one contract version to the next.</summary>
    TSelf AddUpcaster<TFrom, TTo>(Upcaster<TFrom, TTo> upcaster)
        where TFrom : IIntegrationEvent
        where TTo : IIntegrationEvent;

    /// <summary>Registers the projection that owns a read model.</summary>
    TSelf AddProjection<TView>(Projection<TView> projection)
        where TView : class;

    /// <summary>Registers a business process.</summary>
    TSelf AddProcess(Process process);
}
