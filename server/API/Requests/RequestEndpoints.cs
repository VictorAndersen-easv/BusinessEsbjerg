using System.Security.Claims;
using Service;

namespace api.Requests;

public static class RequestEndpoints
{
    public static void MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        // Everything in this group needs a valid token
        var group = app.MapGroup("/api/requests").WithTags("Requests").RequireAuthorization();

        // GET /api/requests/mine
        group.MapGet("/mine", async (ClaimsPrincipal principal, RequestService requests) =>
        {
            // The id comes from the signed token, NOT from the URL. That is what stops
            // one employee from reading another employee's requests.
            var employeeId = int.Parse(principal.FindFirstValue("sub")!);
            return Results.Ok(await requests.GetForEmployeeAsync(employeeId));
        });
    }
}