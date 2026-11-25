using System.Security.Claims;
using api.Auth;
using Service;

namespace api.Balances;

public static class BalanceEndpoints
{
    public static void MapBalanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/balances")
            .WithTags("Balances")
            .AddEndpointFilter<ProblemFilter>()
            .RequireAuthorization();

        // GET /api/balances/mine   (employee logins): the id comes from the token, not the URL
        group.MapGet("/mine", async (ClaimsPrincipal principal, BalanceService balances) =>
        {
            if (!int.TryParse(principal.FindFirstValue("employee_id"), out var employeeId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Only employee logins have a holiday balance.");
            }

            return Results.Ok(await balances.GetForEmployeeAsync(employeeId, Today()));
        });

        // GET /api/balances/employee/{id}   (accountant or system owner)
        group.MapGet("/employee/{employeeId:int}", async (int employeeId, BalanceService balances) =>
                Results.Ok(await balances.GetForEmployeeAsync(employeeId, Today())))
            .RequireAuthorization(Policies.Staff);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);
}