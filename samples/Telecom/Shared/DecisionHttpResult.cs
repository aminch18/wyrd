using Wyrd;

namespace Telecom.Shared;

internal static class DecisionHttpResult
{
    public static IResult From(Decision decision)
    {
        if (decision is Decision.Rejected rejected)
        {
            return Results.Conflict(new { Error = rejected.Error.ToString() });
        }

        return Results.Accepted();
    }
}
