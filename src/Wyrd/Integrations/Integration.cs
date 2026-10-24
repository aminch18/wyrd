namespace Wyrd;

/// <summary>
/// The only place where Wyrd code performs I/O: the single owner of a command that is carried out by another system
/// (a payment provider, an email service). Three outcomes, like a slice: the event (accepted), the error (a business
/// rejection, never retried) — and an exception, a transient failure that the process's retry policy handles.
/// </summary>
/// <example>
/// <code>
/// public sealed class PaymentGateway(HttpClient http) : Integration&lt;ChargeCard, CardCharged, PaymentError&gt;
/// {
///     public override async Task&lt;Result&lt;CardCharged, PaymentError&gt;&gt; ExecuteAsync(
///         ChargeCard command, IntegrationContext context, CancellationToken cancellationToken) { ... }
/// }
/// </code>
/// </example>
public abstract class Integration<TCommand, TEvent, TError> : IIntegrationHandler
    where TCommand : ICommand
    where TEvent : IDomainEvent
    where TError : notnull
{
    /// <summary>
    /// Carries the command out. <see cref="IntegrationContext.IdempotencyKey"/> is the same on every retry and
    /// redelivery: pass it to the other system so a repeated call never repeats the effect.
    /// </summary>
    public abstract Task<Result<TEvent, TError>> ExecuteAsync(TCommand command, IntegrationContext context, CancellationToken cancellationToken);

    /// <summary>What the command is about, for journals and logs (e.g. the order id). Defaults to the command's type.</summary>
    public virtual object KeyOf(TCommand command) => typeof(TCommand).Name;

    Type ICommandOwner.Command => typeof(TCommand);

    Type ICommandOwner.Event => typeof(TEvent);

    Type ICommandOwner.Error => typeof(TError);

    object ICommandOwner.KeyOf(ICommand command) => KeyOf((TCommand)command);

    async Task<Decision> IIntegrationHandler.ExecuteAsync(ICommand command, IntegrationContext context, CancellationToken cancellationToken) =>
        (await ExecuteAsync((TCommand)command, context, cancellationToken).ConfigureAwait(false)).Match<Decision>(
            accepted => new Decision.Accepted(accepted),
            rejected => new Decision.Rejected(rejected));
}

/// <summary>What an integration knows about the call it is making.</summary>
/// <param name="IdempotencyKey">The command's message id: stable across retries and redeliveries.</param>
/// <param name="Attempt">1 for the first try; higher when the process retries after a transient failure.</param>
public sealed record IntegrationContext(Guid IdempotencyKey, int Attempt);

/// <summary>The single owner of a command type: a slice or an integration.</summary>
internal interface ICommandOwner
{
    Type Command { get; }

    Type Event { get; }

    Type Error { get; }

    object KeyOf(ICommand command);
}

internal interface IIntegrationHandler : ICommandOwner
{
    Task<Decision> ExecuteAsync(ICommand command, IntegrationContext context, CancellationToken cancellationToken);
}
