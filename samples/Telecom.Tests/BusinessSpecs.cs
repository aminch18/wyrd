using Telecom;
using Wyrd;
using Wyrd.Runtime;
using Wyrd.Testing;

namespace Telecom.Tests;

public class BusinessSpecs
{
    internal static Offer Offer(string id = "S1", string number = "+34600000001", string profile = "P1", long epoch = 1) =>
        new(id, $"customer-{id}", number, epoch, profile, 25m);
    internal static readonly DateTimeOffset Window = new(2026, 10, 26, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task confirmation_and_provisioning_are_insufficient_for_billing()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await runtime.SendAsync(new ContractSubscription(Offer()));
        await runtime.PublishAsync(new PortConfirmedV1("S1", Offer().Msisdn, 1, Window, Window.AddHours(2)));
        Assert.Equal(PortPhase.Confirmed, runtime.StateOf<NumberPort>(Offer().Msisdn).Phase);
        Assert.True(runtime.StateOf<Activation>("S1").Provisioned);
        Assert.False(runtime.StateOf<Billing>("S1").Started);
        await runtime.PublishAsync(new PortCompletedV1("S1", Offer().Msisdn, 1));
        Assert.False(runtime.StateOf<Billing>("S1").Started);
        await runtime.PublishAsync(new ConnectivityVerifiedV1("S1", "P1", 1));
        Assert.True(runtime.StateOf<Activation>("S1").Ready);
        Assert.True(runtime.StateOf<Billing>("S1").Started);
    }

    [Fact]
    public async Task different_request_ids_contend_on_the_number_not_the_process_key()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await Task.WhenAll(runtime.SendAsync(new ContractSubscription(Offer())), runtime.SendAsync(new ContractSubscription(Offer("S2", epoch: 2))));
        Assert.Single(runtime.Views<PaymentBillingStatus>());
        var owner = runtime.StateOf<NumberPort>(Offer().Msisdn).Owner!.SubscriptionId;
        Assert.IsType<Subscription.Declined>(runtime.StateOf<Subscription>(owner == "S1" ? "S2" : "S1"));
    }

    [Fact]
    public async Task shared_profile_cannot_be_provisioned_for_two_customers()
    {
        var providers = new DeterministicProviders();
        using var runtime = TelecomModule.CreateRuntime(providers);
        await runtime.SendAsync(new ContractSubscription(Offer()));
        await runtime.SendAsync(new ContractSubscription(Offer("S2", "+34600000002")));
        Assert.Equal("customer-S1", runtime.StateOf<ProfileLease>("P1").CustomerId);
        Assert.Equal(1, providers.ProvisionCount);
        Assert.False(runtime.StateOf<Activation>("S2").Provisioned);
        Assert.Contains(runtime.Views<IncidentItem>(), i => i.SubscriptionId == "S2" && i.Open);
    }

    [Fact]
    public async Task lost_successful_capture_is_retried_with_one_external_effect()
    {
        var providers = new DeterministicProviders { LoseCaptureReply = true };
        using var runtime = TelecomModule.CreateRuntime(providers);
        await runtime.SendAsync(new ContractSubscription(Offer()));
        Assert.Equal(1, providers.CaptureCount);
        Assert.False(runtime.StateOf<Activation>("S1").Paid);
        await runtime.AdvanceAsync(5.Minutes);
        Assert.Equal(1, providers.CaptureCount);
        Assert.True(runtime.StateOf<Activation>("S1").Paid);
        var receipt = runtime.StateOf<Payment>("S1").Receipt!;
        await runtime.PublishAsync(new PaymentConfirmedV1("S1", receipt));
        await runtime.PublishAsync(new PaymentConfirmedV1("S1", receipt));
        // A second logical command ID also cannot produce a second capture at the provider.
        await runtime.SendAsync(new CapturePayment(Offer()));
        Assert.Equal(1, providers.CaptureCount);
        var paid = runtime.StateOf<Payment>("S1");
        await runtime.SendAsync(new RecordCapture(Offer(), paid.Receipt!));
        await runtime.SendAsync(new RecordCapture(Offer(), paid.Receipt!));
        Assert.Equal(PaymentPhase.Captured, runtime.StateOf<Payment>("S1").Phase);
    }

    [Fact]
    public async Task reconciliation_recovers_unknown_capture_without_recharging()
    {
        var provider = new ReplyBlackout();
        using var runtime = TelecomModule.CreateRuntime(provider);
        await runtime.SendAsync(new ContractSubscription(Offer()));
        await runtime.AdvanceAsync(10.Minutes);
        Assert.Equal(ProcessStatus.Suspended, runtime.StatusOf(Flows.PaymentCollection, "S1"));
        await runtime.SendAsync(new ReconcilePayment(Offer(), 1));
        Assert.True(runtime.StateOf<Activation>("S1").Paid);
        Assert.Equal(1, provider.World.CaptureCount);
        // Reconciliation does not pretend to have resumed the original process.
        Assert.Equal(ProcessStatus.Suspended, runtime.StatusOf(Flows.PaymentCollection, "S1"));
    }

    [Fact]
    public async Task cancellation_request_keeps_ownership_and_late_confirmation_cannot_regress_completion()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await runtime.SendAsync(new ContractSubscription(Offer()));
        await runtime.SendAsync(new RequestPortCancellation("S1", Offer().Msisdn, 1));
        await runtime.PublishAsync(new PortConfirmedV1("S1", Offer().Msisdn, 1, Window, Window.AddHours(2)));
        Assert.Equal(PortPhase.CancellationRequested, runtime.StateOf<NumberPort>(Offer().Msisdn).Phase);
        await runtime.SendAsync(new ContractSubscription(Offer("S2", epoch: 2)));
        Assert.IsType<Subscription.Declined>(runtime.StateOf<Subscription>("S2"));
        await runtime.PublishAsync(new PortCompletedV1("S1", Offer().Msisdn, 1));
        Assert.Equal(PortPhase.Completed, runtime.StateOf<NumberPort>(Offer().Msisdn).Phase);
        var decision = await runtime.SendAsync(new RequestPortCancellation("S1", Offer().Msisdn, 1));
        Assert.Equal(RuleError.Irreversible, ((Decision.Rejected)decision).Error);
        await runtime.SendAsync(new ApplyPortNotice("S1", Offer().Msisdn, 1, PortFact.Confirmed, Window, Window.AddHours(2)));
        Assert.Equal(PortPhase.Completed, runtime.StateOf<NumberPort>(Offer().Msisdn).Phase);
    }

    [Fact]
    public async Task cancelled_number_reuse_rejects_old_commands_even_after_delivery_retention()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await runtime.SendAsync(new ContractSubscription(Offer()));
        await runtime.SendAsync(new RequestPortCancellation("S1", Offer().Msisdn, 1));
        await runtime.PublishAsync(new PortCancelledV1("S1", Offer().Msisdn, 1));
        await runtime.SendAsync(new ContractSubscription(Offer("S2", profile: "P2", epoch: 2)));
        var late = await runtime.SendAsync(new ClaimNumber(Offer()));
        Assert.IsType<Decision.Rejected>(late);
        var outcome = await runtime.SendAsync(new ApplyPortNotice("S1", Offer().Msisdn, 1, PortFact.Completed, default, default));
        Assert.IsType<Decision.Rejected>(outcome);
        Assert.Equal("S2", runtime.StateOf<NumberPort>(Offer().Msisdn).Owner!.SubscriptionId);
    }

    [Fact]
    public async Task replaced_profile_requires_its_own_connectivity_proof()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await runtime.SendAsync(new ContractSubscription(Offer()));
        await runtime.PublishAsync(new PortCompletedV1("S1", Offer().Msisdn, 1));
        await runtime.SendAsync(new AssignProfile("S1", "customer-S1", "P2", 2));
        await runtime.PublishAsync(new ConnectivityVerifiedV1("S1", "P1", 1));
        Assert.False(runtime.StateOf<Billing>("S1").Started);
        Assert.Equal("P2", runtime.StateOf<Activation>("S1").ProfileId);
        await runtime.PublishAsync(new ConnectivityVerifiedV1("S1", "P2", 2));
        Assert.True(runtime.StateOf<Billing>("S1").Started);
        Assert.Equal("P2", runtime.StateOf<Billing>("S1").ProfileId);
        Assert.Equal("customer-S1", runtime.StateOf<ProfileLease>("P1").CustomerId);
    }

    [Fact]
    public async Task completed_port_with_no_connectivity_opens_incident_without_undoing_port()
    {
        using var runtime = TelecomModule.CreateRuntime();
        await runtime.SendAsync(new ContractSubscription(Offer()));
        await runtime.PublishAsync(new PortCompletedV1("S1", Offer().Msisdn, 1));
        await runtime.AdvanceAsync(31.Minutes);
        Assert.Equal(PortPhase.Completed, runtime.StateOf<NumberPort>(Offer().Msisdn).Phase);
        Assert.False(runtime.StateOf<Billing>("S1").Started);
        Assert.Contains(runtime.Views<IncidentItem>(), i => i.Kind == "connectivity" && i.Open);
        Assert.Equal(ProcessStatus.Compensated, runtime.StatusOf(Flows.ProfileProvisioning, "S1/P1/1"));
        // An expired process will not consume a late confirmation; recovery requires a fresh attempt.
        await runtime.PublishAsync(new ConnectivityVerifiedV1("S1", "P1", 1));
        Assert.False(runtime.StateOf<Billing>("S1").Started);
        await runtime.SendAsync(new AssignProfile("S1", "customer-S1", "P2", 2));
        await runtime.PublishAsync(new ConnectivityVerifiedV1("S1", "P2", 2));
        Assert.True(runtime.StateOf<Billing>("S1").Started);
    }

    [Fact]
    public async Task lost_provision_reply_and_early_connectivity_are_recovered_on_retry()
    {
        var providers = new DeterministicProviders { LoseProvisionReply = true };
        using var runtime = TelecomModule.CreateRuntime(providers);
        await runtime.SendAsync(new ContractSubscription(Offer()));
        await runtime.PublishAsync(new ConnectivityVerifiedV1("S1", "P1", 1));
        await runtime.PublishAsync(new PortCompletedV1("S1", Offer().Msisdn, 1));
        Assert.False(runtime.StateOf<Billing>("S1").Started);
        await runtime.AdvanceAsync(5.Minutes);
        Assert.Equal(1, providers.ProvisionCount);
        Assert.True(runtime.StateOf<Billing>("S1").Started);
    }

    [Fact]
    public async Task nontrivial_incident_history_round_trips_and_replays_recorded_replies()
    {
        using var runtime = TelecomModule.CreateRuntime(new DeterministicProviders { LoseProvisionReply = true });
        await runtime.SendAsync(new ContractSubscription(Offer()));
        await runtime.AdvanceAsync(31.Minutes);
        var history = runtime.HistoryOf(Flows.ProfileProvisioning, "S1/P1/1");
        Assert.Contains(history.Entries, e => e.Kind == HistoryKind.TimedOut);
        var json = history.ToJson();
        var report = await Urd.ReplayAsync(TelecomModule.CreateRuntime, Flows.ProfileProvisioning, InstanceHistory.FromJson(json));
        report.ThrowIfDiverged();
    }

    [Fact]
    public async Task rejected_admission_compensates_only_the_unsubmitted_number_reservation()
    {
        var providers = new DeterministicProviders();
        using var runtime = TelecomModule.CreateRuntime(providers);
        var offer = Offer() with { InitialAmount = 300m };
        await runtime.SendAsync(new ContractSubscription(offer));
        Assert.Equal(ProcessStatus.Compensated, runtime.StatusOf(Flows.Contracting, "S1"));
        Assert.Equal(PortPhase.Cancelled, runtime.StateOf<NumberPort>(offer.Msisdn).Phase);
        Assert.Equal(0, providers.CaptureCount);
        Assert.IsType<Decision.Rejected>(await runtime.SendAsync(new ClaimNumber(offer)));
        Assert.Equal(PortPhase.Cancelled, runtime.StateOf<NumberPort>(offer.Msisdn).Phase);
    }

    [Fact]
    public async Task wrong_epoch_completion_cannot_satisfy_the_current_port_wait()
    {
        using var runtime = TelecomModule.CreateRuntime();
        var offer = Offer();
        await runtime.SendAsync(new ContractSubscription(offer));
        await runtime.PublishAsync(new PortCompletedV1(offer.SubscriptionId, offer.Msisdn, 99));
        Assert.Equal(ProcessStatus.Awaiting, runtime.StatusOf(Flows.PortSubmission, Flows.PortKey(offer.SubscriptionId, offer.Msisdn, offer.Epoch)));
        Assert.Equal(PortPhase.Submitted, runtime.StateOf<NumberPort>(offer.Msisdn).Phase);
        await runtime.PublishAsync(new PortCompletedV1(offer.SubscriptionId, offer.Msisdn, 1));
        Assert.Equal(PortPhase.Completed, runtime.StateOf<NumberPort>(offer.Msisdn).Phase);
    }

    internal sealed class ReplyBlackout : IProviderApi
    {
        public DeterministicProviders World { get; } = new();
        public bool Restored { get; set; }
        public async Task<ProviderReply> CallAsync(ProviderRequest request, Guid key, CancellationToken ct)
        {
            var reply = await World.CallAsync(request, key, ct);
            if (!Restored && request.Action == ProviderAction.Capture) throw new IOException("All capture responses lost");
            return reply;
        }
    }
}
