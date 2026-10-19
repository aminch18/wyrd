namespace Wyrd;

/// <summary>
/// An intention: <i>please do this</i>. Named in the imperative (<c>ChargePayment</c>).
/// It is <b>sent</b> to exactly one owner, which may accept or reject it.
/// </summary>
public interface ICommand;

/// <summary>A fact: something that already happened. It is <b>published</b>, never sent, and can't be rejected.</summary>
public interface IEvent;

/// <summary>
/// A fact inside one bounded context. Named in the past tense (<c>PaymentCharged</c>).
/// Private to its context: it can be refactored freely.
/// </summary>
public interface IDomainEvent : IEvent;

/// <summary>
/// A fact published to other bounded contexts. A <b>public, versioned contract</b>: mark it with
/// <see cref="IntegrationEventAttribute"/> and produce it from domain events with a <see cref="Publication{TDomainEvent, TIntegrationEvent}"/>.
/// </summary>
public interface IIntegrationEvent : IEvent;

/// <summary>Names and versions an integration event contract, e.g. <c>[IntegrationEvent("payments.payment-charged", Version = 1)]</c>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventAttribute(string name) : Attribute
{
    /// <summary>The stable public contract name, shared by all versions of this integration event.</summary>
    public string Name { get; } = name;

    /// <summary>The contract version published by this event type. Defaults to version 1.</summary>
    public int Version { get; set; } = 1;
}
