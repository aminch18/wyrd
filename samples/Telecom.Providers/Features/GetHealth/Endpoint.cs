using MinApiLib.Endpoints;

namespace Telecom.Providers.Features.GetHealth;

public sealed record HealthEndpoint() : Get("/health")
{
    public static IResult Handle() => Results.Ok();
}
