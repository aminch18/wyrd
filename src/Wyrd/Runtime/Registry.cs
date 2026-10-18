using Wyrd.Processes;

namespace Wyrd.Runtime;

/// <summary>What an application is made of: slices, publications, upcasters and processes. Validated on registration.</summary>
internal sealed class Registry
{
    private readonly Dictionary<Type, ISliceHandler> _slices = [];
    private readonly List<IPublicationHandler> _publications = [];
    private readonly Dictionary<(string Name, int Version), IUpcasterHandler> _upcasters = [];
    private readonly List<Process> _processes = [];
    private readonly List<IProjectionHandler> _projections = [];

    public IReadOnlyList<IProjectionHandler> Projections => _projections;

    public void Add(IProjectionHandler projection)
    {
        if (_projections.Any(p => p.View == projection.View))
        {
            throw new InvalidOperationException($"{projection.View.Name} already has a projection. A view has exactly one owner.");
        }

        _projections.Add(projection);
    }

    public IReadOnlyList<Process> Processes => _processes;

    public void Add(ISliceHandler slice)
    {
        if (_slices.TryGetValue(slice.Command, out var owner))
        {
            throw new InvalidOperationException(
                $"{slice.Command.Name} is already owned by {owner.GetType().Name}. A command has exactly one owner.");
        }

        _slices.Add(slice.Command, slice);
    }

    public void Add(IPublicationHandler publication) => _publications.Add(publication);

    public void Add(IUpcasterHandler upcaster)
    {
        if (upcaster.From.Name != upcaster.To.Name || upcaster.To.Version != upcaster.From.Version + 1)
        {
            throw new InvalidOperationException(
                $"An upcaster moves one contract one version forward, but {upcaster.GetType().Name} maps {upcaster.From} to {upcaster.To}.");
        }

        if (!_upcasters.TryAdd((upcaster.From.Name, upcaster.From.Version), upcaster))
        {
            throw new InvalidOperationException($"There is already an upcaster from {upcaster.From}.");
        }
    }

    public void Add(Process process)
    {
        if (_processes.Any(p => p.Name == process.Name))
        {
            throw new InvalidOperationException($"Process '{process.Name}' is registered more than once.");
        }

        _processes.Add(process);
    }

    public ISliceHandler? FindOwner(Type command) => _slices.GetValueOrDefault(command);

    public ISliceHandler OwnerOf(Type command) =>
        FindOwner(command) ?? throw new InvalidOperationException(
            $"No slice owns {command.Name}. Register its owner with AddSlice().");

    public IEnumerable<IPublicationHandler> PublicationsOf(Type domainEvent) => _publications.Where(p => p.Source == domainEvent);

    /// <summary>The versions of a contract this application publishes.</summary>
    public IEnumerable<int> PublishedVersions(string contract) =>
        _publications.Select(p => Contract.Of(p.Target)).Where(c => c.Name == contract).Select(c => c.Version);

    public IUpcasterHandler? Upcaster(string contract, int fromVersion) => _upcasters.GetValueOrDefault((contract, fromVersion));

    /// <summary>Swaps a process for a new definition with the same name, as a redeploy does (tests, replay).</summary>
    public void Redeploy(Process process)
    {
        var index = _processes.FindIndex(p => p.Name == process.Name);
        _processes[index >= 0 ? index : throw new InvalidOperationException($"Process '{process.Name}' is not registered.")] = process;
    }

    public Process ProcessNamed(string name) => _processes.Single(p => p.Name == name);
}
