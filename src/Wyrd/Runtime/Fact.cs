namespace Wyrd.Runtime;

/// <summary>
/// Something that happened, as delivered to subscribers. A domain event has one form; an integration event
/// may be published in several contract versions at once (during a migration), but it is still one fact.
/// </summary>
internal sealed class Fact(IReadOnlyList<IEvent> versions)
{
    public IReadOnlyList<IEvent> Versions { get; } = versions;

    public static Fact Of(IEvent @event) => new([@event]);
}
