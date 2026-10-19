using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Wyrd.Runtime;

/// <summary>W3C diagnostic context. Never used for message identity, correlation, decisions or replay.</summary>
internal sealed record TraceContext(string Parent, string? State = null)
{
    [JsonIgnore]
    public ActivityContext Context => ActivityContext.TryParse(Parent, State, isRemote: true, out var context) ? context : default;
}

/// <summary>BCL instrumentation only. Collectors opt in with AddSource("Wyrd").</summary>
internal static class Tracing
{
    private static readonly ActivitySource Source = new("Wyrd");
    private static readonly AsyncLocal<Handling?> Current = new();

    public static bool IsEnabled => Current.Value is not null;

    public static Handling? Begin(Envelope envelope) => Source.HasListeners() ? new Handling(envelope) : null;

    public static TraceContext? Capture(StoredInstance? instance = null)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Current.Value?.Start(instance?.Trace) ?? Activity.Current;
        // One event can feed several instances. Keep each outgoing command in its own instance's trace.
        return instance?.Trace is { } trace && activity?.TraceId != trace.Context.TraceId
            ? trace
            : activity?.Id is { } id ? new TraceContext(id, activity.TraceStateString) : instance?.Trace;
    }

    // Urd may answer without running a command handler or having an ambient Wyrd activity.
    public static TraceContext? ReplyContext(Envelope command) => Current.Value is null ? command.Trace : Capture() ?? command.Trace;

    public static Activity? Operator(string operation, StoredInstance instance)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var parent = instance.Trace?.Context ?? Activity.Current?.Context ?? default;
        var caller = Activity.Current?.Context ?? default;
        var links = caller.TraceId != default && parent.TraceId != caller.TraceId
            ? new[] { new ActivityLink(caller) } : null;
        var activity = Source.StartActivity("wyrd " + operation, ActivityKind.Internal, parent, links: links);
        activity?.SetTag("wyrd.process.name", instance.Process);
        activity?.SetTag("wyrd.process.key", instance.Key);
        activity?.SetTag("wyrd.process.status", instance.Status.ToString());
        return activity;
    }

    public static void Key(string key) => Current.Value?.Start()?.SetTag("wyrd.key", key);

    public static void Outcome(ReplyOutcome outcome)
    {
        if (IsEnabled)
        {
            Outcome(outcome.ToString(), outcome);
        }
    }

    public static void Outcome(string outcome, ReplyOutcome? reply = null)
    {
        var activity = Current.Value?.Start();
        activity?.SetTag("wyrd.outcome", outcome);
        if (reply is not null)
        {
            activity?.SetTag("wyrd.reply.outcome", reply.ToString());
        }
    }

    public static void Instance(StoredInstance instance) => Current.Value?.Instance(instance);

    public static void Event(string name, StoredInstance instance, int? step = null, int? attempt = null)
    {
        var activity = Current.Value?.Start(instance.Trace);
        if (activity?.IsAllDataRequested == true)
        {
            activity.AddEvent(new ActivityEvent(name, tags: new ActivityTagsCollection
            {
                { "wyrd.process.name", instance.Process },
                { "wyrd.process.key", instance.Key },
                { "wyrd.step", step },
                { "wyrd.attempt", attempt },
            }));
        }
    }

    internal sealed class Handling : IDisposable
    {
        private readonly Handling? _previous = Current.Value;
        private readonly Envelope _envelope;
        private readonly ActivityContext _incoming;
        private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
        private Activity? _activity;
        private bool _initialized;
        private (string Process, string Key)? _instance;

        public Handling(Envelope envelope)
        {
            _envelope = envelope;
            _incoming = envelope.Trace?.Context ?? Activity.Current?.Context ?? default;
            Current.Value = this;
            // An event's existing correlation logic supplies its instance context. Don't correlate it a second time
            // just for tracing. Unmatched events still get exactly one activity when the scope ends.
            if (envelope.Kind == MessageKind.Command)
            {
                Start();
            }
        }

        public Activity? Start(TraceContext? instance = null)
        {
            var parent = instance?.Context ?? _incoming;
            if (_incoming.TraceId != default && parent.TraceId == _incoming.TraceId)
            {
                parent = _incoming; // Preserve the immediate producer span when it already belongs to this trace.
            }
            if (_initialized)
            {
                if (parent.TraceId != default && _activity is { } existing && parent.TraceId != existing.TraceId
                    && !existing.Links.Any(link => link.Context == parent))
                {
                    existing.AddLink(new ActivityLink(parent));
                }

                return _activity;
            }

            _initialized = true;
            var links = _incoming.TraceId != default && parent.TraceId != _incoming.TraceId
                ? new[] { new ActivityLink(_incoming) } : null;
            _activity = Source.StartActivity($"wyrd {_envelope.Kind} {_envelope.Type}", ActivityKind.Consumer,
                parent, links: links, startTime: _started);
            if (_activity?.IsAllDataRequested == true)
            {
                _activity.SetTag("messaging.operation", "process");
                _activity.SetTag("messaging.operation.type", "process");
                _activity.SetTag("messaging.message.id", _envelope.MessageId.ToString());
                _activity.SetTag("wyrd.message.kind", _envelope.Kind.ToString());
                _activity.SetTag("wyrd.message.type", _envelope.Type);
                _activity.SetTag("wyrd.outcome", "handled");
                _activity.SetTag("wyrd.attempt", _envelope.Process?.Attempt ?? 1);
                if (_envelope.Process is { } reference)
                {
                    _activity.SetTag("wyrd.process.name", reference.Process);
                    _activity.SetTag("wyrd.process.key", reference.Key);
                    _activity.SetTag("wyrd.step", reference.Step);
                    _activity.SetTag("wyrd.compensating", reference.Compensating);
                }

                if (_envelope.Stream is { } stream)
                {
                    _activity.SetTag("wyrd.key", stream.Key);
                }
            }

            return _activity;
        }

        public void Instance(StoredInstance instance)
        {
            var activity = Start(instance.Trace);
            _instance ??= (instance.Process, instance.Key);
            if (_instance == (instance.Process, instance.Key))
            {
                activity?.SetTag("wyrd.process.name", instance.Process);
                activity?.SetTag("wyrd.process.key", instance.Key);
                activity?.SetTag("wyrd.process.status", instance.Status.ToString());
            }
        }

        public void Failed(Exception exception)
        {
            Start()?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            Outcome("exception");
        }

        public void Dispose()
        {
            Start()?.Dispose();
            Current.Value = _previous;
        }
    }
}
