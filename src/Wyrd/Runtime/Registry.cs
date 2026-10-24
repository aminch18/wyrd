using System.Collections.Concurrent;
using Wyrd.Processes;

namespace Wyrd.Runtime;

/// <summary>What an application is made of: slices, publications, upcasters and processes. Validated on registration.</summary>
internal sealed class Registry
{
    private readonly Dictionary<Type, ICommandOwner> _owners = [];
    private readonly List<IPublicationHandler> _publications = [];
    private readonly Dictionary<(string Name, int Version), IUpcasterHandler> _upcasters = [];
    private readonly List<Process> _processes = [];
    private readonly List<IProjectionHandler> _projections = [];

    public IReadOnlyList<IProjectionHandler> Projections => _projections;

    /// <summary>
    /// The views whose projections have caught up from existing states (see <see cref="Projection{TView}.Seed{TState}"/>).
    /// Once caught up, always caught up: hosts remember it here so events don't ask the store again.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _caughtUp = [];

    /// <summary>The projections that seed from states: the only ones that can be catching up.</summary>
    private readonly List<IProjectionHandler> _seeding = [];

    public bool IsCaughtUp(IProjectionHandler projection) => !_seeding.Contains(projection) || _caughtUp.ContainsKey(projection.View.FullName!);

    public void CaughtUp(IProjectionHandler projection) => _caughtUp[projection.View.FullName!] = true;

    /// <summary>Projections still catching up from existing states.</summary>
    public IEnumerable<IProjectionHandler> CatchingUp => _seeding.Where(p => !IsCaughtUp(p));

    /// <summary>Whether any projection is still catching up: checked on every new stream, so it allocates nothing.</summary>
    public bool AnyCatchingUp
    {
        get
        {
            foreach (var projection in _seeding)
            {
                if (!_caughtUp.ContainsKey(projection.View.FullName!))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void Add(IProjectionHandler projection)
    {
        if (_projections.Any(p => p.View == projection.View))
        {
            throw new InvalidOperationException($"{projection.View.Name} already has a projection. A view has exactly one owner.");
        }

        _projections.Add(projection);
        if (projection.Seeds.Any())
        {
            _seeding.Add(projection);
        }
    }

    public IReadOnlyList<Process> Processes => _processes;

    public void Add(ICommandOwner owner)
    {
        if (_owners.TryGetValue(owner.Command, out var existing))
        {
            throw new InvalidOperationException(
                $"{owner.Command.Name} is already owned by {existing.GetType().Name}. A command has exactly one owner.");
        }

        _owners.Add(owner.Command, owner);
        _validated = false;
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
        _validated = false;
    }

    public ICommandOwner? FindOwner(Type command) => _owners.GetValueOrDefault(command);

    public ICommandOwner OwnerOf(Type command) =>
        FindOwner(command) ?? throw new InvalidOperationException(
            $"Nothing owns {command.Name}. Register its owner with AddSlice() or AddIntegration().");

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
        _validated = false;
    }

    private bool _validated;

    /// <summary>
    /// Checks what can only be checked once everything is registered: every event a step reads is produced by an
    /// earlier step's owner or awaited before it (an undo, before its own step). Runs once, before the first message.
    /// </summary>
    public void Validate()
    {
        if (_validated)
        {
            return;
        }

        foreach (var process in _processes)
        {
            List<Type> produced = [];
            foreach (var node in process.Nodes)
            {
                if (node is StepNode step)
                {
                    Check(process, step.Action, produced);
                    if (step.Compensation is { } undo)
                    {
                        Check(process, undo, produced);
                    }

                    if (step.Action.Output is { } command && FindOwner(command) is { } owner)
                    {
                        produced.Add(owner.Event);
                    }
                }
                else if (node is AwaitNode wait)
                {
                    produced.Add(wait.Event);
                }
            }
        }

        _validated = true;
    }

    private static void Check(Process process, StepRef step, List<Type> produced)
    {
        foreach (var read in step.Reads)
        {
            if (!produced.Any(read.IsAssignableFrom))
            {
                throw new InvalidOperationException(
                    $"Process '{process.Name}': step '{step}' reads {read.Name}, but no earlier step produces it"
                    + (produced.Count == 0 ? "." : $" (earlier steps produce {string.Join(", ", produced.Select(t => t.Name))}).")
                    + " A step can read the events of the steps before it, and an undo those before its own step.");
            }
        }
    }

    /// <summary>When set, only this process reacts to events (Urd replays one instance at a time).</summary>
    public string? Only { get; set; }

    public IEnumerable<Process> Active => Only is null ? _processes : _processes.Where(p => p.Name == Only);

    public Process ProcessNamed(string name) => _processes.Single(p => p.Name == name);
}
