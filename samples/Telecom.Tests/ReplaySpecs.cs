using Wyrd.Runtime;
using Wyrd.Testing;

namespace Telecom.Tests;

public class ReplaySpecs
{
    [Fact]
    public async Task checked_in_incident_replays_without_calling_external_systems()
    {
        var world = new CountingProvider();
        var history = InstanceHistory.FromJson(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "connectivity-timeout.history.json")));
        var result = await Urd.ReplayAsync(() => TelecomModule.CreateRuntime(world), Flows.ProfileProvisioning, history);
        result.ThrowIfDiverged();
        Assert.Equal(0, world.Calls);
    }

    [Fact]
    public async Task replay_reports_the_first_divergent_command_instead_of_claiming_success()
    {
        var history = InstanceHistory.FromJson(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "connectivity-timeout.history.json")));
        var entries = history.Entries.ToArray();
        var sent = Array.FindIndex(entries, e => e.Kind == HistoryKind.Sent);
        entries[sent] = entries[sent] with { Payload = entries[sent].Payload!.Replace("profile-1", "unexpected-profile", StringComparison.Ordinal) };
        var result = await Urd.ReplayAsync(TelecomModule.CreateRuntime, Flows.ProfileProvisioning, history with { Entries = entries });
        Assert.True(result.Diverged);
        Assert.NotNull(result.Divergence);
    }

    private sealed class CountingProvider : IProviderApi
    {
        public int Calls { get; private set; }
        public Task<ProviderReply> CallAsync(ProviderRequest request, Guid deliveryKey, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Replay must use recorded replies.");
        }
    }
}
