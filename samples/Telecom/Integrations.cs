using System.Collections.Concurrent;
using Wyrd;

namespace Telecom;

public enum ProviderError { Declined, NotFound, Conflict }
public enum ProviderAction { Capture, FindCapture, SubmitPort, CancelPort, Provision }
public sealed record ProviderRequest(ProviderAction Action, string LogicalKey, string Resource, string Owner, decimal Amount, int Attempt);
public sealed record ProviderReply(string? Receipt, ProviderError? Error = null);

// This port models the three outside systems, not a new Wyrd abstraction.
public interface IProviderApi
{
    Task<ProviderReply> CallAsync(ProviderRequest request, Guid deliveryKey, CancellationToken cancellationToken);
}

/// <summary>Deterministic, thread-safe external world for tests. A new instance belongs to each simulated future.</summary>
public sealed class DeterministicProviders : IProviderApi
{
    private readonly ConcurrentDictionary<string, (ProviderRequest Request, string Receipt)> _effects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _lost = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    public bool LoseCaptureReply { get; init; }
    public bool LoseProvisionReply { get; init; }
    public bool DeclineCapture { get; init; }
    public int CaptureCount => _effects.Values.Count(e => e.Request.Action == ProviderAction.Capture);
    public int ProvisionCount => _effects.Values.Count(e => e.Request.Action == ProviderAction.Provision);

    public Task<ProviderReply> CallAsync(ProviderRequest request, Guid deliveryKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            // Logical obligation uniqueness outlives Wyrd's inbox and covers a second message ID too.
            if (request.Action == ProviderAction.FindCapture)
                return Task.FromResult(_effects.TryGetValue(request.LogicalKey, out var captured)
                    ? new ProviderReply(captured.Receipt) : new ProviderReply(null, ProviderError.NotFound));
            if (DeclineCapture && request.Action == ProviderAction.Capture)
                return Task.FromResult(new ProviderReply(null, ProviderError.Declined));
            if (_effects.TryGetValue(request.LogicalKey, out var old) && old.Request != request)
                return Task.FromResult(new ProviderReply(null, ProviderError.Conflict));
            if (request.Action == ProviderAction.Provision && _effects.Values.Any(e => e.Request.Action == ProviderAction.Provision
                && e.Request.Resource == request.Resource && e.Request.Owner != request.Owner))
                return Task.FromResult(new ProviderReply(null, ProviderError.Conflict));
            var effect = _effects.GetOrAdd(request.LogicalKey, _ => (request, $"receipt-{request.LogicalKey}"));
            if (((LoseCaptureReply && request.Action == ProviderAction.Capture) || (LoseProvisionReply && request.Action == ProviderAction.Provision))
                && _lost.TryAdd(request.LogicalKey, 0)) throw new IOException("Provider committed; response was lost.");
            return Task.FromResult(new ProviderReply(effect.Receipt));
        }
    }
}

public sealed record CapturePayment(Offer Offer) : ICommand;
public sealed record FindPayment(Offer Offer) : ICommand;
public sealed record CaptureReceived(string SubscriptionId, string Receipt) : IDomainEvent;
public sealed record SubmitPort(Offer Offer) : ICommand;
public sealed record PortSubmissionReceived(string SubscriptionId, string Receipt) : IDomainEvent;
public sealed record CancelPort(Offer Offer) : ICommand;
public sealed record PortCancellationReceived(string SubscriptionId, string Receipt) : IDomainEvent;
public sealed record ProvisionProfile(string SubscriptionId, string CustomerId, string ProfileId, int Attempt) : ICommand;
public sealed record ProvisionReceived(string SubscriptionId, string ProfileId, int Attempt, string Receipt) : IDomainEvent;

public sealed class CapturePaymentIntegration(IProviderApi provider) : Integration<CapturePayment, CaptureReceived, ProviderError>
{
    public override object KeyOf(CapturePayment command) => command.Offer.SubscriptionId;
    public override async Task<Result<CaptureReceived, ProviderError>> ExecuteAsync(CapturePayment command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var reply = await provider.CallAsync(PaymentRequest(command.Offer), context.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        return reply.Error is { } error ? error : new CaptureReceived(command.Offer.SubscriptionId, reply.Receipt!);
    }
    public static ProviderRequest PaymentRequest(Offer offer) => new(ProviderAction.Capture, $"capture/{offer.SubscriptionId}", offer.SubscriptionId, offer.CustomerId, offer.InitialAmount, 0);
}
public sealed class FindPaymentIntegration(IProviderApi provider) : Integration<FindPayment, CaptureReceived, ProviderError>
{
    public override async Task<Result<CaptureReceived, ProviderError>> ExecuteAsync(FindPayment command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var reply = await provider.CallAsync(CapturePaymentIntegration.PaymentRequest(command.Offer) with { Action = ProviderAction.FindCapture }, context.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        return reply.Error is { } error ? error : new CaptureReceived(command.Offer.SubscriptionId, reply.Receipt!);
    }
}
public sealed class SubmitPortIntegration(IProviderApi provider) : Integration<SubmitPort, PortSubmissionReceived, ProviderError>
{
    public override async Task<Result<PortSubmissionReceived, ProviderError>> ExecuteAsync(SubmitPort command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var o = command.Offer;
        var reply = await provider.CallAsync(new(ProviderAction.SubmitPort, $"port/{o.Msisdn}/{o.Epoch}", o.Msisdn, o.SubscriptionId, 0, 0), context.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        return reply.Error is { } error ? error : new PortSubmissionReceived(o.SubscriptionId, reply.Receipt!);
    }
}
public sealed class CancelPortIntegration(IProviderApi provider) : Integration<CancelPort, PortCancellationReceived, ProviderError>
{
    public override async Task<Result<PortCancellationReceived, ProviderError>> ExecuteAsync(CancelPort command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var o = command.Offer;
        var reply = await provider.CallAsync(new(ProviderAction.CancelPort, $"cancel/{o.Msisdn}/{o.Epoch}", o.Msisdn, o.SubscriptionId, 0, 0), context.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        return reply.Error is { } error ? error : new PortCancellationReceived(o.SubscriptionId, reply.Receipt!);
    }
}
public sealed class ProvisionProfileIntegration(IProviderApi provider) : Integration<ProvisionProfile, ProvisionReceived, ProviderError>
{
    public override object KeyOf(ProvisionProfile command) => command.SubscriptionId;
    public override async Task<Result<ProvisionReceived, ProviderError>> ExecuteAsync(ProvisionProfile command, IntegrationContext context, CancellationToken cancellationToken)
    {
        var reply = await provider.CallAsync(new(ProviderAction.Provision, $"profile/{command.ProfileId}/{command.Attempt}", command.ProfileId,
            command.CustomerId, 0, command.Attempt), context.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        return reply.Error is { } error ? error : new ProvisionReceived(command.SubscriptionId, command.ProfileId, command.Attempt, reply.Receipt!);
    }
}
