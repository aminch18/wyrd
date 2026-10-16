namespace Wyrd;

/// <summary>State rebuilt from domain events. <c>Evolve</c> must be pure.</summary>
public interface IState
{
    internal object Apply(IDomainEvent domainEvent);
}

/// <inheritdoc cref="IState"/>
public interface IState<TSelf> : IState
    where TSelf : IState<TSelf>
{
    static abstract TSelf Initial { get; }

    TSelf Evolve(IDomainEvent domainEvent);

    object IState.Apply(IDomainEvent domainEvent) => Evolve(domainEvent);
}
