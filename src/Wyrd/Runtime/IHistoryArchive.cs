namespace Wyrd.Runtime;

/// <summary>
/// Where the histories of finished instances go before retention removes them: blob storage, a cold table, a data lake.
/// The hot tables stay small, the process audit trail is kept, and an archived instance still replays as a test
/// (<see cref="InstanceHistory.FromJson"/>, then Urd). Without an archive, expiring a finished instance deletes it.
/// </summary>
public interface IHistoryArchive
{
    /// <summary>
    /// Keeps <paramref name="histories"/>, and returns only once they are safely stored: the instances are removed
    /// after. An exception keeps the whole batch for the next pass, oldest first, so a history you can never store (too
    /// large for the target, say) blocks every removal after it: put it somewhere else and return, rather than throw
    /// for it. A host shows the finished instances kept past their retention (<c>ExpiredKept</c> in the EF Core host's
    /// queue health), so an alert sees a stuck archive. The same history can come again (when the host stopped between
    /// the two), and a key can be reused once its instance is removed: store each history by its process, its key and
    /// the time of its first entry, overwriting.
    /// </summary>
    Task ArchiveAsync(IReadOnlyList<InstanceHistory> histories, CancellationToken cancellationToken);
}
