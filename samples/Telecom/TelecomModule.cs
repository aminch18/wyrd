using Wyrd.Runtime;

namespace Telecom;

public static class TelecomModule
{
    public static InMemoryRuntime CreateRuntime() => CreateRuntime(new DeterministicProviders());

    public static InMemoryRuntime CreateRuntime(IProviderApi providers)
    {
        return Register(new InMemoryRuntime(TelecomJson.Default), providers);
    }

    public static TRuntime Register<TRuntime>(TRuntime runtime, IProviderApi providers)
        where TRuntime : IWyrdBuilder<TRuntime>
    {
        return runtime
            // Subscription admission.
            .AddSlice(new ContractSubscriptionSlice())
            .AddSlice(new AdmitSubscriptionSlice())
            .AddSlice(new DeclineSubscriptionSlice())
            .AddProcess(Flows.Contracting)

            // Number ownership and the provider's porting protocol.
            .AddSlice(new ClaimNumberSlice())
            .AddSlice(new ReleaseUnsubmittedNumberSlice())
            .AddSlice(new MarkPortSubmittedSlice())
            .AddSlice(new ApplyPortNoticeSlice())
            .AddSlice(new RequestPortCancellationSlice())
            .AddIntegration(new SubmitPortIntegration(providers))
            .AddIntegration(new CancelPortIntegration(providers))
            .AddProcess(Flows.PortSubmission)
            .AddProcess(Flows.PortConfirmation)
            .AddProcess(Flows.PortCompletion)
            .AddProcess(Flows.PortCancellation)
            .AddProcess(Flows.CancellationConfirmation)
            .AddProjection(new PortabilityDetailProjection())

            // Initial obligation, capture and read-only reconciliation.
            .AddSlice(new OpenPaymentSlice())
            .AddSlice(new RecordCaptureSlice())
            .AddSlice(new RecordProviderCaptureSlice())
            .AddSlice(new ReconcilePaymentSlice())
            .AddIntegration(new CapturePaymentIntegration(providers))
            .AddIntegration(new FindPaymentIntegration(providers))
            .AddProcess(Flows.PaymentOpening)
            .AddProcess(Flows.PaymentCollection)
            .AddProcess(Flows.PaymentConfirmation)
            .AddProcess(Flows.PaymentReconciliation)
            .AddProjection(new PaymentBillingProjection())

            // Profile ownership, provisioning and connectivity.
            .AddSlice(new AssignProfileSlice())
            .AddSlice(new ClaimProfileSlice())
            .AddIntegration(new ProvisionProfileIntegration(providers))
            .AddProcess(Flows.InitialProfile)
            .AddProcess(Flows.ProfileProvisioning)

            // Independently verified activation facts and operational billing.
            .AddSlice(new ObserveActivationSlice())
            .AddSlice(new RecordCompletedPortSlice())
            .AddSlice(new AssessActivationSlice())
            .AddSlice(new BeginBillingSlice())
            .AddPublication(new OperationalPublication())
            .AddProcess(Flows.PaymentEvidence)
            .AddProcess(Flows.PortEvidence)
            .AddProcess(Flows.ActivationAssessment)
            .AddProcess(Flows.BillingCommencement)
            .AddProjection(new CustomerProgressProjection())

            // Business attention queue.
            .AddSlice(new OpenIncidentSlice())
            .AddSlice(new ResolveIncidentSlice())
            .AddProjection(new IncidentProjection());
    }
}
