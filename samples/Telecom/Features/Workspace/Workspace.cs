using Wyrd.Runtime;

namespace Telecom.Features.Workspace;

public sealed record SubscriptionCard(CustomerProgress Progress, PaymentBillingStatus? Payment, int OpenIncidents);

public sealed record SubscriptionWorkspace(
    CustomerProgress Progress,
    PaymentBillingStatus? Payment,
    PortabilityDetail? Port,
    IReadOnlyList<IncidentItem> Incidents,
    IReadOnlyList<ProcessCard> Processes,
    WorkspaceActions Actions,
    DomainSnapshot Model);

public sealed record ProcessCard(string Name, string Key, string? Status, IReadOnlyList<HistoryEntry> History);

public sealed record ActionAvailability(bool Enabled, string Reason);

public sealed record WorkspaceActions(
    ActionAvailability CancelPort,
    ActionAvailability ReplaceProfile,
    bool CanResumePayment,
    string SuggestedProfile,
    int NextAttempt,
    int NextReview);

public sealed record DomainSnapshot(Activation Activation, NumberPort Number, Payment Payment);

public static class WorkspaceQueries
{
    public static async Task<IReadOnlyList<SubscriptionCard>> ListAsync(IWyrdRuntime runtime, CancellationToken cancellationToken)
    {
        var customers = await runtime.ViewsAsync<CustomerProgress>(cancellationToken);
        var payments = await runtime.ViewsAsync<PaymentBillingStatus>(cancellationToken);
        var incidents = await runtime.ViewsAsync<IncidentItem>(cancellationToken);
        var billingById = payments.ToDictionary(payment => payment.SubscriptionId, StringComparer.Ordinal);
        var incidentsById = incidents.Where(incident => incident.Open)
            .GroupBy(incident => incident.SubscriptionId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return customers.OrderBy(customer => customer.SubscriptionId, StringComparer.Ordinal)
            .Select(customer => new SubscriptionCard(customer,
                billingById.GetValueOrDefault(customer.SubscriptionId),
                incidentsById.GetValueOrDefault(customer.SubscriptionId)))
            .ToArray();
    }

    public static async Task<SubscriptionWorkspace?> FindAsync(IWyrdRuntime runtime, string id, CancellationToken cancellationToken)
    {
        var progress = await runtime.ViewAsync<CustomerProgress>(id, cancellationToken);
        if (progress is null)
        {
            return null;
        }

        var paymentView = await runtime.ViewAsync<PaymentBillingStatus>(id, cancellationToken);
        var portView = await runtime.ViewAsync<PortabilityDetail>(id, cancellationToken);
        var allIncidents = await runtime.ViewsAsync<IncidentItem>(cancellationToken);
        var incidents = allIncidents.Where(incident => incident.SubscriptionId == id && incident.Open).ToArray();

        var processes = new List<ProcessCard>
        {
            await ReadProcessAsync(runtime, Flows.Contracting, id, cancellationToken),
            await ReadProcessAsync(runtime, Flows.PaymentCollection, id, cancellationToken),
        };
        if (portView is not null)
        {
            var key = Flows.PortKey(id, portView.Msisdn, portView.Epoch);
            processes.Add(await ReadProcessAsync(runtime, Flows.PortSubmission, key, cancellationToken));
        }
        if (progress.ProfileId is not null)
        {
            var key = Flows.ProfileKey(id, progress.ProfileId, progress.Attempt);
            processes.Add(await ReadProcessAsync(runtime, Flows.ProfileProvisioning, key, cancellationToken));
        }
        processes.Add(await ReadProcessAsync(runtime, Flows.BillingCommencement, id, cancellationToken));

        // Diagnostic/advisory reads only. Business queries above use projections; commands always decide again.
        var activation = await runtime.StateOfAsync<Activation>(id, cancellationToken);
        var payment = await runtime.StateOfAsync<Payment>(id, cancellationToken);
        var number = NumberPort.Initial;
        if (progress.Msisdn is not null)
        {
            number = await runtime.StateOfAsync<NumberPort>(progress.Msisdn, cancellationToken);
        }

        var actions = AdviseActions(id, activation, number, payment, processes);
        return new SubscriptionWorkspace(progress, paymentView, portView, incidents, processes, actions,
            new DomainSnapshot(activation, number, payment));
    }

    private static WorkspaceActions AdviseActions(string id, Activation activation, NumberPort number, Payment payment, IReadOnlyList<ProcessCard> processes)
    {
        var owner = number.Owner;
        var cancellation = number.CheckCancellation(new RequestPortCancellation(id, owner?.Msisdn ?? "", owner?.Epoch ?? 0));
        var nextAttempt = activation.Attempt + 1;
        var profile = $"{id[..Math.Min(id.Length, 40)]}-esim-{nextAttempt}";
        var assignment = new AssignProfile(id, activation.CustomerId ?? payment.Offer?.CustomerId ?? "", profile, nextAttempt);
        var replacement = activation.CheckProfileAssignment(assignment);
        var canResume = processes.Any(process => process.Name == "PaymentCollection" && process.Status == nameof(ProcessStatus.Suspended));
        return new WorkspaceActions(
            Availability(cancellation, "Request provider cancellation; retain the number until acknowledgement."),
            Availability(replacement, "Reserve a different profile; previous provisioning and connectivity evidence will be discarded."),
            canResume, profile, nextAttempt, payment.Review + 1);
    }

    private static ActionAvailability Availability(RuleError? error, string allowedReason)
    {
        if (error is null)
        {
            return new ActionAvailability(true, allowedReason);
        }
        var reason = error.Value switch
        {
            RuleError.Irreversible => "This operation is closed: completed portability and operational service cannot be rolled back.",
            RuleError.NotReady => "Provider submission is not acknowledged yet; reconcile its outcome before requesting cancellation.",
            RuleError.WrongOwner => "The current resource does not belong to this subscription and request.",
            RuleError.StaleAttempt => "Use the next profile attempt.",
            RuleError.InvalidInput => "The operation does not have valid customer and resource identities yet.",
            _ => $"The domain rejects this operation: {error.Value}.",
        };
        return new ActionAvailability(false, reason);
    }

    private static async Task<ProcessCard> ReadProcessAsync(IWyrdRuntime runtime, Wyrd.Processes.Process process, string key, CancellationToken cancellationToken)
    {
        var status = await runtime.StatusOfAsync(process, key, cancellationToken);
        var history = await runtime.HistoryOfAsync(process, key, cancellationToken);
        return new ProcessCard(history?.Process ?? process.Name, key, status?.ToString(), history?.Entries ?? []);
    }
}
