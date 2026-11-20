using Service;

namespace api;

/// <summary>
/// Wraps an endpoint: if the service throws a rule violation or a "not found",
/// answer with a clean 400 / 404 (and a readable message) instead of crashing with a 500.
/// Runs inside the endpoint, so the CORS headers are still added and the browser can read the message.
/// </summary>
public class ProblemFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (RuleViolationException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Rule violation", detail: ex.Message);
        }
        catch (NotFoundException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: ex.Message);
        }
    }
}