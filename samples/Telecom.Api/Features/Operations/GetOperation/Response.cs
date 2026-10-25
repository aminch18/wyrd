using Wyrd.Runtime;

namespace Telecom.Api.Features.Operations;

public sealed record OperationDetail(
    PortabilityDetail? Port,
    PaymentBillingStatus? PaymentAndBilling,
    ProcessStatus? Capture,
    ProcessStatus? PortSubmission,
    ProcessStatus? Profile);
