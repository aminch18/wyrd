namespace Wyrd.EntityFrameworkCore;

/// <summary>
/// Where a durable host removes what its <see cref="Wyrd.Runtime.Retention"/> no longer keeps
/// (<see cref="EfCoreRuntimeOptions.Cleanup"/>).
/// </summary>
public enum CleanupMode
{
    /// <summary>
    /// Workers (<see cref="EfCoreRuntime{TContext}.RunAsync"/>) remove a bounded amount every
    /// <see cref="EfCoreRuntimeOptions.CleanupInterval"/>, between messages. One process does everything: right for one
    /// instance, or a few.
    /// </summary>
    Workers,

    /// <summary>
    /// Workers never remove anything: a separate service does, with <see cref="EfCoreRuntime{TContext}.RunCleanupAsync"/>
    /// (a long-running cleaner) or <see cref="EfCoreRuntime{TContext}.ForgetExpiredAsync(int, CancellationToken)"/> (a scheduled job). Cleanup
    /// never takes a worker's time, and many instances of an API don't all clean the same tables at once. Nothing
    /// expires until that service runs. <see cref="QueueHealth.ExpiredKept"/> shows it only for finished instances (and
    /// not at all when they are kept forever), so also alert when the cleaner itself stops running. That service's runtime is configured
    /// like the workers' (retention, transport, history archive): see <see cref="EfCoreRuntime{TContext}.RunCleanupAsync"/>.
    /// </summary>
    Separate,
}
