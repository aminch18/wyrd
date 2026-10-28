using Wyrd;
using Wyrd.Testing;

namespace Telecom.Tests;

public class ProjectionSpecs
{
    [Fact]
    public void payment_and_billing_facts_commute_even_with_duplicate_deliveries()
    {
        var offer = BusinessSpecs.Offer();
        IEvent[] facts = [new SubscriptionOpened(offer), new PaymentOpened(offer), new PaymentCaptured(offer, "receipt"), new BillingBegan("S1", "P1", 1)];
        foreach (var permutation in Permutations(facts))
        {
            Scenario.For(new PaymentBillingProjection()).Given([.. permutation, .. permutation])
                .Then("S1", new PaymentBillingStatus("S1", true, "receipt", true));
        }
    }

    [Fact]
    public void customer_view_preserves_independent_port_and_payment_facts()
    {
        var o = BusinessSpecs.Offer();
        IEvent[] facts = [new SubscriptionOpened(o), new PaymentCaptured(o, "r"), new NumberChanged(o, PortPhase.Completed, default, default), new ServiceOperational("S1", "P1", 1)];
        foreach (var permutation in Permutations(facts))
            Scenario.For(new CustomerProgressProjection()).Given(permutation)
                .Then("S1", new CustomerProgress("S1", o.Msisdn, true, false, true, Operational: true, CustomerId: o.CustomerId, InitialAmount: o.InitialAmount));
    }

    // Enumerate interleavings of independent streams; do not claim same-stream order is arbitrary.
    internal static IEnumerable<T[]> Permutations<T>(T[] values) => values.Length == 0 ? [[]]
        : values.SelectMany((item, i) => Permutations(values.Where((_, j) => i != j).ToArray()).Select(tail => new[] { item }.Concat(tail).ToArray()));
}
