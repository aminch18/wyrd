namespace Wyrd.Runtime;

public enum ProcessStatus
{
    Running,
    Awaiting,
    Completed,
    Compensated,

    /// <summary>
    /// Wyrd couldn't resolve something — retries exhausted, an undo that never succeeded — and stopped instead of
    /// guessing. Visible and recoverable: resume it (after a fix) or compensate it. See <c>Reason</c>.
    /// </summary>
    Suspended,
}
