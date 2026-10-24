namespace Wyrd.Runtime;

/// <summary>The execution state of one process instance, for progress reporting and operator decisions.</summary>
public enum ProcessStatus
{
    /// <summary>Executing forward steps or compensating previous work.</summary>
    Running,
    /// <summary>Waiting for a correlated event within its business deadline.</summary>
    Awaiting,
    /// <summary>All forward steps finished.</summary>
    Completed,
    /// <summary>The required undos and compensation outcome finished.</summary>
    Compensated,

    /// <summary>
    /// Wyrd couldn't resolve something — retries exhausted, an undo that never succeeded — and stopped instead of
    /// guessing. Visible and recoverable: resume it (after a fix) or compensate it. See <c>Reason</c>.
    /// </summary>
    Suspended,
}
