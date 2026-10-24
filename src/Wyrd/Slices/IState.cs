namespace Wyrd;

/// <summary>State evolved by accepted domain events and persisted as a snapshot. <c>Evolve</c> must be pure.</summary>
public interface IState
{
    internal object Apply(IDomainEvent domainEvent);
}

/// <inheritdoc cref="IState"/>
public interface IState<TSelf> : IState
    where TSelf : IState<TSelf>
{
    /// <summary>The state used when no snapshot exists for a key.</summary>
    static abstract TSelf Initial { get; }

    /// <summary>Returns the next immutable state after an accepted domain event. Keep this function pure.</summary>
    TSelf Evolve(IDomainEvent domainEvent);

    object IState.Apply(IDomainEvent domainEvent) => Evolve(domainEvent);
}
