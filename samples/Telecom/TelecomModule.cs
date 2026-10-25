using Wyrd.Runtime;

namespace Telecom;

public static class TelecomModule
{
    public static InMemoryRuntime CreateRuntime() => CreateRuntime(new DeterministicProviders());
    public static InMemoryRuntime CreateRuntime(IProviderApi providers) => Register(new InMemoryRuntime(TelecomJson.Default), providers);
    public static TRuntime Register<TRuntime>(TRuntime runtime, IProviderApi providers) where TRuntime : IWyrdBuilder<TRuntime> => runtime
        .AddSlice(new AdmitSubscriptionSlice()).AddSlice(new ReleaseUnsubmittedNumberSlice())
        .AddSlice(new ContractSubscriptionSlice()).AddSlice(new DeclineSubscriptionSlice())
        .AddSlice(new ClaimNumberSlice()).AddSlice(new MarkPortSubmittedSlice()).AddSlice(new ApplyPortNoticeSlice()).AddSlice(new RequestPortCancellationSlice())
        .AddSlice(new RecordProviderCaptureSlice()).AddSlice(new OpenPaymentSlice()).AddSlice(new RecordCaptureSlice()).AddSlice(new ReconcilePaymentSlice())
        .AddSlice(new AssignProfileSlice()).AddSlice(new ClaimProfileSlice()).AddSlice(new ObserveActivationSlice())
        .AddSlice(new RecordCompletedPortSlice()).AddSlice(new AssessActivationSlice())
        .AddSlice(new BeginBillingSlice()).AddSlice(new OpenIncidentSlice()).AddSlice(new ResolveIncidentSlice())
        .AddIntegration(new CapturePaymentIntegration(providers)).AddIntegration(new FindPaymentIntegration(providers))
        .AddIntegration(new SubmitPortIntegration(providers)).AddIntegration(new CancelPortIntegration(providers))
        .AddIntegration(new ProvisionProfileIntegration(providers))
        .AddPublication(new OperationalPublication())
        .AddProjection(new CustomerProgressProjection()).AddProjection(new PortabilityDetailProjection())
        .AddProjection(new PaymentBillingProjection()).AddProjection(new IncidentProjection())
        .AddProcess(Flows.Contracting).AddProcess(Flows.PaymentOpening).AddProcess(Flows.PaymentCollection).AddProcess(Flows.PaymentReconciliation)
        .AddProcess(Flows.PortSubmission).AddProcess(Flows.PortConfirmation).AddProcess(Flows.PortCompletion)
        .AddProcess(Flows.PortCancellation).AddProcess(Flows.CancellationConfirmation)
        .AddProcess(Flows.InitialProfile).AddProcess(Flows.ProfileProvisioning)
        .AddProcess(Flows.PaymentConfirmation).AddProcess(Flows.PaymentEvidence).AddProcess(Flows.PortEvidence).AddProcess(Flows.ActivationAssessment).AddProcess(Flows.BillingCommencement);
}
