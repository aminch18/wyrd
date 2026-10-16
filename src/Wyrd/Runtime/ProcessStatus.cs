namespace Wyrd.Runtime;

public enum ProcessStatus
{
    Running,
    Awaiting,
    Completed,
    Compensated,

    /// <summary>A compensation failed: the instance needs human attention.</summary>
    Failed,
}
