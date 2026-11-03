using Wyrd;
using Wyrd.Runtime;
using Wyrd.Testing;

namespace Telecom.Tests;

public class HostileFuturesSpecs
{
    private static readonly Invariant Safety = new("billing and resource safety", Check);

    // Skuld's public hook is end-of-future. Also check after every externally observable action below.
    private static string? Check(InMemoryRuntime runtime)
    {
        foreach (var id in new[] { "S1", "S2" })
        {
            var activation = runtime.StateOf<Activation>(id);
            if (runtime.StateOf<Billing>(id).Started && (!activation.Ready || !activation.Operational))
                return $"{id} billed without verified operational service";
            if (activation.Provisioned && runtime.StateOf<ProfileLease>(activation.ProfileId!).SubscriptionId != id)
                return $"{id} provisioned a profile owned by another subscription";
        }
        return null;
    }

    [Fact]
    public async Task duplicated_reordered_and_faulted_futures_preserve_business_safety()
    {
        var report = await Skuld.SimulateAsync(
            () => TelecomModule.CreateRuntime(new DeterministicProviders { LoseCaptureReply = true, LoseProvisionReply = true }),
            async runtime =>
            {
                await runtime.SendAsync(new ContractSubscription(BusinessSpecs.Offer()));
                Assert.Null(Check(runtime));
                await runtime.SendAsync(new ContractSubscription(BusinessSpecs.Offer("S2", epoch: 2)));
                Assert.Null(Check(runtime));
                var confirmed = new PortConfirmedV1("S1", BusinessSpecs.Offer().Msisdn, 1, BusinessSpecs.Window, BusinessSpecs.Window.AddHours(2));
                await runtime.PublishAsync(confirmed);
                await runtime.PublishAsync(confirmed);
                Assert.Null(Check(runtime));
                await runtime.PublishAsync(new ConnectivityVerifiedV1("S1", "P1", 1));
                Assert.Null(Check(runtime));
                await runtime.PublishAsync(new PortCompletedV1("S1", BusinessSpecs.Offer().Msisdn, 1));
                Assert.Null(Check(runtime));
                await runtime.AdvanceAsync(10.Minutes);
                Assert.Null(Check(runtime));
            },
            [Invariant.ProcessesSettle, Safety],
            new SimulationOptions { Runs = 300, Seed = 27100, FaultRate = 0.15, DuplicateRate = 0.25, ReorderRate = 0.65, SettleFor = 3.Days, ShrinkBudget = 50 });
        report.ThrowIfViolated();
    }

    [Fact]
    public async Task late_connectivity_is_parked_during_suspension_and_consumed_after_resume()
    {
        var provider = new ProvisionBlackout();
        using var runtime = TelecomModule.CreateRuntime(provider);
        await runtime.SendAsync(new ContractSubscription(BusinessSpecs.Offer()));
        await runtime.AdvanceAsync(10.Minutes);
        Assert.Equal(ProcessStatus.Suspended, runtime.StatusOf(Flows.ProfileProvisioning, "S1/P1/1"));
        await runtime.PublishAsync(new ConnectivityVerifiedV1("S1", "P1", 1));
        await runtime.PublishAsync(new PortCompletedV1("S1", BusinessSpecs.Offer().Msisdn, 1));
        Assert.False(runtime.StateOf<Billing>("S1").Started);
        provider.Restored = true;
        await runtime.ResumeAsync(Flows.ProfileProvisioning, "S1/P1/1");
        Assert.True(runtime.StateOf<Billing>("S1").Started);
        Assert.Equal(1, provider.World.ProvisionCount);
    }

    [Fact]
    public async Task replacement_cancellation_and_late_confirmations_remain_safe_under_reordering()
    {
        var report = await Skuld.SimulateAsync(TelecomModule.CreateRuntime, async runtime =>
        {
            await runtime.SendAsync(new ContractSubscription(BusinessSpecs.Offer()));
            await runtime.AdvanceAsync(5.Minutes);
            Assert.Null(Check(runtime));
            await runtime.SendAsync(new AssignProfile("S1", "customer-S1", "P2", 2));
            Assert.Null(Check(runtime));
            await runtime.SendAsync(new RequestPortCancellation("S1", BusinessSpecs.Offer().Msisdn, 1));
            await runtime.PublishAsync(new PortConfirmedV1("S1", BusinessSpecs.Offer().Msisdn, 1, BusinessSpecs.Window, BusinessSpecs.Window.AddHours(2)));
            Assert.Null(Check(runtime));
            await runtime.PublishAsync(new PortCompletedV1("S1", BusinessSpecs.Offer().Msisdn, 1));
            await runtime.PublishAsync(new ConnectivityVerifiedV1("S1", "P1", 1));
            Assert.Null(Check(runtime));
            await runtime.PublishAsync(new ConnectivityVerifiedV1("S1", "P2", 2));
            Assert.Null(Check(runtime));
        }, [Invariant.ProcessesSettle, Safety], new SimulationOptions
        {
            Runs = 200, Seed = 32000, FaultRate = 0.15, DuplicateRate = 0.3, ReorderRate = 0.7,
            SettleFor = 3.Days, ShrinkBudget = 50,
        });
        report.ThrowIfViolated();
    }

    private sealed class ProvisionBlackout : IProviderApi
    {
        public DeterministicProviders World { get; } = new();
        public bool Restored { get; set; }
        public async Task<ProviderReply> CallAsync(ProviderRequest request, Guid deliveryKey, CancellationToken cancellationToken)
        {
            var receipt = await World.CallAsync(request, deliveryKey, cancellationToken);
            if (!Restored && request.Action == ProviderAction.Provision) throw new IOException("Response lost after provisioning");
            return receipt;
        }
    }
}
