using Wyrd.Processes;

namespace Wyrd.Runtime;

/// <summary>
/// A deterministic, single-threaded runtime with a virtual clock.
/// Same inputs, same journal, every time: the foundation for testing, simulation (Skuld) and replay (Urd).
/// </summary>
public sealed class InMemoryRuntime
{
    private const string NoProcess = "-";

    private readonly List<Process> _processes = [];
    private readonly Dictionary<Type, ISliceHandler> _slices = [];
    private readonly List<IPublicationHandler> _publications = [];
    private readonly Dictionary<(Type State, object Key), object> _states = [];
    private readonly Dictionary<(string Process, object Key), ProcessInstance> _instances = [];
    private readonly Dictionary<Type, int> _faults = [];
    private readonly Queue<IEvent> _pending = new();
    private readonly List<IDomainEvent> _history = [];
    private readonly List<IIntegrationEvent> _published = [];
    private readonly List<JournalEntry> _journal = [];

    /// <summary>Registers the single owner of a command type.</summary>
    public InMemoryRuntime AddSlice<TCommand, TState, TEvent, TError>(Slice<TCommand, TState, TEvent, TError> slice)
        where TCommand : ICommand
        where TState : IState<TState>
        where TEvent : IDomainEvent
        where TError : notnull
    {
        ArgumentNullException.ThrowIfNull(slice);
        if (_slices.TryGetValue(typeof(TCommand), out var owner))
        {
            throw new InvalidOperationException(
                $"{typeof(TCommand).Name} is already owned by {owner.GetType().Name}. A command has exactly one owner.");
        }

        _slices.Add(typeof(TCommand), slice);
        return this;
    }

    /// <summary>Registers a mapping from a domain event to an integration event.</summary>
    public InMemoryRuntime AddPublication<TDomainEvent, TIntegrationEvent>(Publication<TDomainEvent, TIntegrationEvent> publication)
        where TDomainEvent : IDomainEvent
        where TIntegrationEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(publication);
        _publications.Add(publication);
        return this;
    }

    public InMemoryRuntime AddProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (_processes.Any(p => p.Name == process.Name))
        {
            throw new InvalidOperationException($"Process '{process.Name}' is registered more than once.");
        }

        _processes.Add(process);
        return this;
    }

    /// <summary>Virtual time elapsed since the runtime started.</summary>
    public TimeSpan Now { get; private set; }

    public IReadOnlyList<JournalEntry> Journal => _journal;

    /// <summary>Every accepted domain event, in order.</summary>
    public IReadOnlyList<IDomainEvent> History => _history;

    /// <summary>Every integration event published to other contexts, in order.</summary>
    public IReadOnlyList<IIntegrationEvent> Published => _published;

    /// <summary>Executes a command coming from outside a process (an API call, a UI action...).</summary>
    public Decision Send(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var key = _slices.TryGetValue(command.GetType(), out var slice) ? slice.KeyOf(command) : null;
        Log(NoProcess, key, $"-> {command.GetType().Name}");
        var decision = Decide(command);
        Log(NoProcess, key, Describe(decision));
        Drain();
        return decision;
    }

    /// <summary>
    /// Delivers a fact from another context (a webhook, a message from another service...).
    /// Only integration events can come from outside: domain events are produced by slices alone.
    /// </summary>
    public void Publish(IIntegrationEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _pending.Enqueue(@event);
        Drain();
    }

    /// <summary>Moves the virtual clock forward and fires any expired timeouts.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        Now += by;

        var expired = _instances.Values
            .Where(i => i.Status == ProcessStatus.Awaiting && i.Deadline <= Now)
            .OrderBy(i => i.Deadline)
            .ToList();

        foreach (var instance in expired)
        {
            Log(instance, $"timeout: {instance.Awaiting!.Event.Name} not received in time");
            instance.Status = ProcessStatus.Running;
            instance.Deadline = null;
            Compensate(instance);
        }

        Drain();
    }

    /// <summary>Makes the next <paramref name="times"/> executions of a command fail transiently.</summary>
    public void FailNext<TCommand>(int times = 1)
        where TCommand : ICommand
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(times, 1);
        _faults[typeof(TCommand)] = _faults.GetValueOrDefault(typeof(TCommand)) + times;
    }

    public ProcessStatus? StatusOf(Process process, object key)
    {
        ArgumentNullException.ThrowIfNull(process);
        return _instances.TryGetValue((process.Name, key), out var instance) ? instance.Status : null;
    }

    public TState StateOf<TState>(object key)
        where TState : IState<TState> =>
        _states.TryGetValue((typeof(TState), key), out var state) ? (TState)state : TState.Initial;

    private Decision Decide(ICommand command)
    {
        if (!_slices.TryGetValue(command.GetType(), out var slice))
        {
            throw new InvalidOperationException(
                $"No slice owns {command.GetType().Name}. Register its owner with AddSlice().");
        }

        var id = (slice.State, slice.KeyOf(command));
        var state = _states.TryGetValue(id, out var current) ? current : slice.Initial;
        var decision = slice.Decide(command, state);

        if (decision is Decision.Accepted accepted)
        {
            _states[id] = ((IState)state).Apply(accepted.Event);
            _history.Add(accepted.Event);
            _pending.Enqueue(accepted.Event);

            foreach (var publication in _publications.Where(p => p.Source == accepted.Event.GetType()))
            {
                var published = publication.Map(accepted.Event);
                _published.Add(published);
                _pending.Enqueue(published);
                Log(NoProcess, id.Item2, $"published {published.GetType().Name}");
            }
        }

        return decision;
    }

    private void Drain()
    {
        while (_pending.TryDequeue(out var @event))
        {
            Route(@event);
        }
    }

    private void Route(IEvent @event)
    {
        var type = @event.GetType();

        foreach (var process in _processes.Where(p => p.Trigger == type))
        {
            var key = process.KeyOf(@event);
            if (_instances.ContainsKey((process.Name, key)))
            {
                Log(process.Name, key, $"duplicate {type.Name} ignored");
                continue;
            }

            var instance = new ProcessInstance(process, key, @event);
            _instances.Add((process.Name, key), instance);
            Log(instance, $"started by {type.Name}");
            Run(instance);
        }

        var waiting = _instances.Values
            .Where(i => i.Awaiting is { } wait && wait.Event == type && Equals(wait.KeyOf!(@event), i.Key))
            .ToList();

        foreach (var instance in waiting)
        {
            Log(instance, $"<- {type.Name}");
            instance.Status = ProcessStatus.Running;
            instance.Deadline = null;
            instance.Position++;
            Run(instance);
        }
    }

    private void Run(ProcessInstance instance)
    {
        var nodes = instance.Process.Nodes;
        while (instance.Position < nodes.Length)
        {
            switch (nodes[instance.Position])
            {
                case StepNode step:
                    if (!TryExecute(instance, step.Action, step.Retry))
                    {
                        Compensate(instance);
                        return;
                    }

                    instance.Completed.Push(step);
                    instance.Position++;
                    break;

                case AwaitNode wait:
                    instance.Status = ProcessStatus.Awaiting;
                    instance.Deadline = Now + wait.Timeout;
                    Log(instance, wait.Timeout is { } t ? $"awaiting {wait.Event.Name} (within {Durations.Format(t)})" : $"awaiting {wait.Event.Name}");
                    return;
            }
        }

        instance.Status = ProcessStatus.Completed;
        Log(instance, "completed");
    }

    private bool TryExecute(ProcessInstance instance, StepRef step, RetryPolicy? retry)
    {
        var command = step.Invoke!(instance.Trigger);
        var retries = retry?.Attempts.Count ?? 0;
        Log(instance, $"-> {command.GetType().Name}");

        for (var attempt = 1; ; attempt++)
        {
            if (!TryInjectFault(command))
            {
                var decision = Decide(command);
                Log(instance, Describe(decision));
                return decision is Decision.Accepted;
            }

            if (attempt > retries)
            {
                Log(instance, retries == 0 ? "transient failure, no retry policy" : "transient failure, retries exhausted");
                return false;
            }

            Log(instance, $"transient failure, retry {attempt}/{retries} after {Durations.Format(retry!.DelayBefore(attempt))}");
        }
    }

    private void Compensate(ProcessInstance instance)
    {
        Log(instance, "compensating");
        while (instance.Completed.TryPop(out var done))
        {
            if (done.Compensation is { } undo && !TryExecute(instance, undo, done.Retry))
            {
                instance.Status = ProcessStatus.Failed;
                Log(instance, $"FAILED: {undo} could not run, needs attention");
                return;
            }
        }

        instance.Status = ProcessStatus.Compensated;
        Log(instance, "compensated");
    }

    private bool TryInjectFault(ICommand command)
    {
        var type = command.GetType();
        if (_faults.GetValueOrDefault(type) == 0)
        {
            return false;
        }

        _faults[type]--;
        return true;
    }

    private static string Describe(Decision decision) => decision switch
    {
        Decision.Accepted accepted => $"ok {accepted.Event.GetType().Name}",
        Decision.Rejected rejected => $"rejected {rejected.Error}",
        _ => throw new InvalidOperationException($"Unknown decision {decision}."),
    };

    private void Log(ProcessInstance instance, string message) => Log(instance.Process.Name, instance.Key, message);

    private void Log(string process, object? key, string message) => _journal.Add(new(Now, process, key, message));
}
