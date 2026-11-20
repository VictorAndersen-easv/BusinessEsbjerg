using System.Security.Claims;
using api.Auth;
using Service;

namespace api.Employees;

public static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/employees")
            .WithTags("Employees")
            .AddEndpointFilter<ProblemFilter>();

        // GET /api/employees   (accountant or system owner)
        group.MapGet("/", async (EmployeeService employees) =>
                Results.Ok(await employees.ListAsync()))
            .RequireAuthorization(Policies.Staff);

        // POST /api/employees   (system owner only): employee + login + first schedule
        group.MapPost("/", async (CreateEmployeeRequest request, ClaimsPrincipal user, EmployeeService employees) =>
            {
                var created = await employees.CreateAsync(request, AccountId(user));
                return Results.Created($"/api/employees/{created.EmployeeId}/schedules", created);
            })
            .RequireAuthorization(Policies.SystemOwner);

        // GET /api/employees/{id}/schedules   (accountant or system owner): full history
        group.MapGet("/{employeeId:int}/schedules", async (int employeeId, EmployeeService employees) =>
                Results.Ok(await employees.GetSchedulesAsync(employeeId)))
            .RequireAuthorization(Policies.Staff);

        // POST /api/employees/{id}/schedules   (accountant or system owner): new schedule from a Monday
        group.MapPost("/{employeeId:int}/schedules", async (
                int employeeId, ChangeScheduleRequest request, ClaimsPrincipal user, EmployeeService employees) =>
                Results.Ok(await employees.ChangeScheduleAsync(employeeId, request, AccountId(user))))
            .RequireAuthorization(Policies.Staff);
    }

    /// <summary>The login that is making the request, read from the signed token.</summary>
    private static int AccountId(ClaimsPrincipal user) => int.Parse(user.FindFirstValue("sub")!);
}