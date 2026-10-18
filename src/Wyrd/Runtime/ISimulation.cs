namespace Wyrd.Runtime;

/// <summary>
/// Lets a simulator (Skuld) perturb delivery deterministically: which message goes next, which is redelivered,
/// which command fails transiently. Without one, the runtime delivers in order, once, without faults.
/// </summary>
internal interface ISimulation
{
    /// <summary>Index of the next message to deliver from the queue (0 = in order).</summary>
    int PickNext(IReadOnlyList<Envelope> queue);

    /// <summary>Whether to deliver this message a second time later (at-least-once delivery).</summary>
    bool Duplicate(Envelope envelope);

    /// <summary>Whether this command, sent by a process, fails transiently.</summary>
    bool Fault(Envelope command, Type commandType);
}
