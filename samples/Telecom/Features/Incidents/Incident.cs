using Wyrd;

namespace Telecom;

public sealed record Incident(bool Open) : IState<Incident>
{
    public static Incident Initial { get; } = new(false);
    public Incident Evolve(IDomainEvent domainEvent) => domainEvent switch
    {
        IncidentOpened => new(true), IncidentResolved => new(false), _ => this,
    };
    public static string Key(string id, string kind, int attempt) => $"{id}/{kind}/{attempt}";
}
