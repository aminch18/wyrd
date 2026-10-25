using Wyrd;

namespace Telecom.Shared;

internal static class DecisionHttpResult
{
    public static IResult From(Decision decision)
    {
        if (decision is Decision.Rejected rejected)
        {
            var body = new { Error = rejected.Error.ToString() };
            if (rejected.Error is RuleError.InvalidInput)
            {
                return Results.BadRequest(body);
            }

            return Results.Conflict(body);
        }

        return Results.Accepted();
    }
}
