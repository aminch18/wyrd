using Wyrd.Testing;

namespace Telecom.Tests;

public class PureDomainSpecs
{
    [Fact]
    public void payment_amount_and_receipt_are_bound_to_the_original_obligation()
    {
        var o = BusinessSpecs.Offer();
        Scenario.For(new RecordCaptureSlice()).Given(new PaymentOpened(o))
            .When(new RecordCapture(o with { InitialAmount = 100m }, "r"))
            .ThenRejected(RuleError.WrongOwner);
        Scenario.For(new RecordCaptureSlice()).Given(new PaymentOpened(o), new PaymentCaptured(o, "r1"))
            .When(new RecordCapture(o, "r2")).ThenRejected(RuleError.ConflictingReceipt);
    }

    [Fact]
    public void an_old_profile_cannot_validate_a_new_attempt()
    {
        Scenario.For(new ObserveActivationSlice())
            .Given(new ProfileAssigned("S1", "C1", "P1", 1), new ProfileAssigned("S1", "C1", "P2", 2))
            .When(new ObserveActivation("S1", ActivationFact.Connectivity, "P1", 1))
            .ThenRejected(RuleError.StaleAttempt);
    }

    [Fact]
    public void business_deadlines_do_not_turn_an_irreversible_port_into_a_reversible_operation()
    {
        var o = BusinessSpecs.Offer();
        Scenario.For(new RequestPortCancellationSlice())
            .Given(new NumberReserved(o), new NumberChanged(o, PortPhase.Completed, default, default))
            .When(new RequestPortCancellation(o.SubscriptionId, o.Msisdn, o.Epoch))
            .ThenRejected(RuleError.Irreversible);
    }

    [Fact]
    public void unknown_submission_requires_reconciliation_before_requesting_external_cancellation()
    {
        var o = BusinessSpecs.Offer();
        Scenario.For(new RequestPortCancellationSlice()).Given(new NumberReserved(o))
            .When(new RequestPortCancellation(o.SubscriptionId, o.Msisdn, o.Epoch))
            .ThenRejected(RuleError.NotReady);
    }

    [Fact]
    public void accepted_admission_fences_local_release_before_any_queued_provider_work_runs()
    {
        var o = BusinessSpecs.Offer();
        Scenario.For(new ReleaseUnsubmittedNumberSlice()).Given(new NumberReserved(o), new SubscriptionAdmitted(o))
            .When(new ReleaseUnsubmittedNumber(o)).ThenRejected(RuleError.Irreversible);
    }

    [Theory]
    [InlineData("34600000001")]
    [InlineData("+34 600000001")]
    [InlineData("+3460000000A")]
    public void phone_numbers_have_one_canonical_key(string number)
    {
        Scenario.For(new ContractSubscriptionSlice()).When(new ContractSubscription(BusinessSpecs.Offer(number: number)))
            .ThenRejected(RuleError.InvalidInput);
    }

    [Fact]
    public void activation_is_a_conjunction_of_independent_verified_facts()
    {
        var ready = new Activation(true, true, "C1", "P1", 1, true, true, false);
        Assert.True(ready.Ready);
        foreach (var incomplete in new[] { ready with { Paid = false }, ready with { Ported = false },
            ready with { Provisioned = false }, ready with { Connected = false } })
            Assert.False(incomplete.Ready);
    }
}
