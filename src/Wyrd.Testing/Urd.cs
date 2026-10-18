using Wyrd.Processes;
using Wyrd.Runtime;

namespace Wyrd.Testing;

/// <summary>
/// Urd, the Norn of what was. Replays a recorded process instance against today's code: the process runs for real,
/// every command it sends is answered with the recorded reply, and the history it produces is compared with the
/// recorded one. The first difference is the finding.
/// </summary>
/// <example>
/// <code>
/// var history = InstanceHistory.FromJson(File.ReadAllText("A-1.history.json"));
/// var report = await Urd.ReplayAsync(ShopModule.CreateRuntime, Processes.OrderFulfillment, history);
/// report.ThrowIfDiverged();
/// </code>
/// </example>
public static class Urd
{
    public static async Task<ReplayReport> ReplayAsync(Func<InMemoryRuntime> system, Process process, InstanceHistory history)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(history);
        if (process.Name != history.Process)
        {
            throw new ArgumentException($"The history is of '{history.Process}', not '{process.Name}'.", nameof(history));
        }

        if (history.Entries.Count == 0 || history.Entries[0].Kind != HistoryKind.Started)
        {
            throw new ArgumentException("A history starts with the event that started the instance.", nameof(history));
        }

        using var runtime = system();
        if (process.Fingerprint != history.Fingerprint)
        {
            return new ReplayReport(history, [], runtime.Journal)
            {
                Reason = $"the structure of {process.Name} changed since this history was recorded "
                    + $"(fingerprint {history.Fingerprint}, now {process.Fingerprint}): replay it against the definition it ran with",
            };
        }

        runtime.ReplayOnly(process.Name);
        runtime.Responder = new Recording(history).Answer;

        Exception? error = null;
        try
        {
            await DriveAsync(runtime, process, history).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = ex;
        }

        IReadOnlyList<HistoryEntry> replayed;
        try
        {
            replayed = runtime.HistoryOf(process, history.Key).Entries;
        }
        catch (InvalidOperationException)
        {
            replayed = [];
        }

        return new ReplayReport(history, replayed, runtime.Journal) { Error = error };
    }

    /// <summary>Delivers what came from outside the instance at the time it came; the clock fires retries and timeouts.</summary>
    private static async Task DriveAsync(InMemoryRuntime runtime, Process process, InstanceHistory history)
    {
        var start = history.Entries[0].At;
        foreach (var entry in history.Entries)
        {
            await AdvanceToAsync(runtime, entry.At - start).ConfigureAwait(false);
            switch (entry.Kind)
            {
                case HistoryKind.Started:
                case HistoryKind.Received:
                    await runtime.InjectAsync(entry.Type!, entry.Payload!).ConfigureAwait(false);
                    break;
                case HistoryKind.Resumed:
                    await runtime.ResumeAsync(process, history.Key).ConfigureAwait(false);
                    break;
                case HistoryKind.CompensationRequested:
                    await runtime.CompensateAsync(process, history.Key).ConfigureAwait(false);
                    break;
            }
        }
    }

    private static async Task AdvanceToAsync(InMemoryRuntime runtime, TimeSpan offset)
    {
        if (offset > runtime.Now)
        {
            await runtime.AdvanceAsync(offset - runtime.Now).ConfigureAwait(false);
        }
    }

    /// <summary>Answers the n-th command the replay sends with the reply recorded for the n-th command sent.</summary>
    private sealed class Recording(InstanceHistory history)
    {
        private int _sent;

        public ReplyMessage? Answer(Envelope command)
        {
            var sends = history.Entries.Select((entry, index) => (entry, index)).Where(e => e.entry.Kind == HistoryKind.Sent).ToList();
            if (_sent >= sends.Count)
            {
                return null; // more commands than recorded: the comparison reports it
            }

            var (sent, at) = sends[_sent++];
            var reply = history.Entries.Skip(at + 1).FirstOrDefault(e =>
                e.Kind == HistoryKind.Replied && e.Step == sent.Step && e.Compensating == sent.Compensating && e.Attempt == sent.Attempt);
            return reply is null ? null : new ReplyMessage(Enum.Parse<ReplyOutcome>(reply.Outcome!), reply.Type, reply.Payload);
        }
    }
}
