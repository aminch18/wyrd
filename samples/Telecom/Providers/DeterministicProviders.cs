using System.Collections.Concurrent;

namespace Telecom;

/// <summary>Deterministic external world for tests. Each simulated future gets its own instance.</summary>
public sealed class DeterministicProviders : IProviderApi
{
    private readonly ConcurrentDictionary<string, (ProviderRequest Request, string Receipt)> _effects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _lost = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public bool LoseCaptureReply { get; init; }
    public bool LoseProvisionReply { get; init; }
    public bool DeclineCapture { get; init; }

    public int CaptureCount => _effects.Values.Count(effect => effect.Request.Action == ProviderAction.Capture);
    public int ProvisionCount => _effects.Values.Count(effect => effect.Request.Action == ProviderAction.Provision);

    public Task<ProviderReply> CallAsync(ProviderRequest request, Guid deliveryKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (request.Action == ProviderAction.FindCapture)
            {
                return Task.FromResult(FindCapture(request.LogicalKey));
            }

            var error = Validate(request);
            if (error is not null)
            {
                return Task.FromResult(new ProviderReply(null, error.Value));
            }

            // Logical obligation uniqueness also covers calls with a different delivery ID.
            var effect = _effects.GetOrAdd(request.LogicalKey, _ => (request, $"receipt-{request.LogicalKey}"));

            if (ShouldLoseReply(request.Action) && _lost.TryAdd(request.LogicalKey, 0))
            {
                throw new IOException("Provider committed; response was lost.");
            }

            return Task.FromResult(new ProviderReply(effect.Receipt));
        }
    }

    private ProviderReply FindCapture(string logicalKey)
    {
        if (_effects.TryGetValue(logicalKey, out var captured))
        {
            return new ProviderReply(captured.Receipt);
        }

        return new ProviderReply(null, ProviderError.NotFound);
    }

    private ProviderError? Validate(ProviderRequest request)
    {
        if (DeclineCapture && request.Action == ProviderAction.Capture)
        {
            return ProviderError.Declined;
        }

        if (_effects.TryGetValue(request.LogicalKey, out var previous) && previous.Request != request)
        {
            return ProviderError.Conflict;
        }

        if (request.Action == ProviderAction.Provision && ProfileBelongsToAnotherCustomer(request))
        {
            return ProviderError.Conflict;
        }

        return null;
    }

    private bool ProfileBelongsToAnotherCustomer(ProviderRequest request)
    {
        return _effects.Values.Any(effect =>
            effect.Request.Action == ProviderAction.Provision
            && effect.Request.Resource == request.Resource
            && effect.Request.Owner != request.Owner);
    }

    private bool ShouldLoseReply(ProviderAction action) => action switch
    {
        ProviderAction.Capture => LoseCaptureReply,
        ProviderAction.Provision => LoseProvisionReply,
        _ => false,
    };
}
