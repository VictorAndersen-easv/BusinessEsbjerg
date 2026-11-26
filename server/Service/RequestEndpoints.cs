using System.Security.Claims;
using api.Auth;
using Service;

namespace api.Requests;

public static class RequestEndpoints
{
    public static void MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        // Everything in this group needs a valid token
        var group = app.MapGroup("/api/requests")
            .WithTags("Requests")
            .AddEndpointFilter<ProblemFilter>()
            .RequireAuthorization();

        // ---------------------------------------------------------------- employee logins

        // GET /api/requests/mine
        group.MapGet("/mine", async (ClaimsPrincipal user, RequestService requests) =>
        {
            // The id comes from the signed token, NOT from the URL. That is what stops
            // one employee from reading another employee's requests.
            var employeeId = EmployeeId(user);
            if (employeeId is null) return NoEmployee();

            return Results.Ok(await requests.GetForEmployeeAsync(employeeId.Value));
        });

        // POST /api/requests/preview: what would this cost, and is it allowed? Saves nothing.
        group.MapPost("/preview", async (RequestInput input, ClaimsPrincipal user, HolidayRequestService service) =>
        {
            var employeeId = EmployeeId(user);
            if (employeeId is null) return NoEmployee();

            return Results.Ok(await service.PreviewAsync(employeeId.Value, input));
        });

        // POST /api/requests: send the request to the accountant
        group.MapPost("/", async (RequestInput input, ClaimsPrincipal user, HolidayRequestService service) =>
        {
            var employeeId = EmployeeId(user);
            if (employeeId is null) return NoEmployee();

            var created = await service.SubmitAsync(employeeId.Value, input);
            return Results.Created($"/api/requests/{created.RequestId}", created);
        });

        // POST /api/requests/{id}/withdraw: take back a request that has not been decided yet
        group.MapPost("/{requestId:int}/withdraw", async (int requestId, ClaimsPrincipal user, HolidayRequestService service) =>
        {
            var employeeId = EmployeeId(user);
            if (employeeId is null) return NoEmployee();

            await service.WithdrawAsync(employeeId.Value, requestId);
            return Results.NoContent();
        });

        // ---------------------------------------------------------------- the accountant

        // GET /api/requests/pending
        group.MapGet("/pending", async (HolidayRequestService service) =>
                Results.Ok(await service.ListPendingAsync()))
            .RequireAuthorization(Policies.Accountant);

        // POST /api/requests/{id}/approve
        group.MapPost("/{requestId:int}/approve", async (
                int requestId, DecisionInput? body, ClaimsPrincipal user, HolidayRequestService service) =>
                Results.Ok(await service.ApproveAsync(requestId, AccountId(user), body?.Note)))
            .RequireAuthorization(Policies.Accountant);

        // POST /api/requests/{id}/decline   (a reason is required)
        group.MapPost("/{requestId:int}/decline", async (
                int requestId, DecisionInput? body, ClaimsPrincipal user, HolidayRequestService service) =>
                Results.Ok(await service.DeclineAsync(requestId, AccountId(user), body?.Note)))
            .RequireAuthorization(Policies.Accountant);
    }

    private static int? EmployeeId(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue("employee_id"), out var id) ? id : null;

    private static int AccountId(ClaimsPrincipal user) => int.Parse(user.FindFirstValue("sub")!);

    private static IResult NoEmployee() =>
        Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden",
            detail: "Only employee logins can do this.");
}