namespace Wyrd;

/// <summary>
/// The explicit boundary between a context's internal facts and its public contracts.
/// The only place where a domain event becomes an integration event. Register several publications
/// for the same domain event to publish two contract versions during a migration.
/// </summary>
public abstract class Publication<TDomainEvent, TIntegrationEvent> : IPublicationHandler
    where TDomainEvent : IDomainEvent
    where TIntegrationEvent : IIntegrationEvent
{
    /// <summary>Purely maps an accepted internal fact to the public contract version represented by this publication.</summary>
    public abstract TIntegrationEvent Map(TDomainEvent domainEvent);

    Type IPublicationHandler.Source => typeof(TDomainEvent);

    Type IPublicationHandler.Target => typeof(TIntegrationEvent);

    IIntegrationEvent IPublicationHandler.Map(IDomainEvent domainEvent) => Map((TDomainEvent)domainEvent);
}

internal interface IPublicationHandler
{
    Type Source { get; }

    Type Target { get; }

    IIntegrationEvent Map(IDomainEvent domainEvent);
}
