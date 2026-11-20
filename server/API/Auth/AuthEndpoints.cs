using System.Security.Claims;
using Infrastructure;
using Infrastructure.Entities;
using LinqToDB;
using LinqToDB.Async;

namespace api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        // POST /api/auth/login   (open to everyone)
        group.MapPost("/login", async (LoginRequest request, ExamDatabase db, TokenService tokens) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var account = await db.UserAccounts.FirstOrDefaultAsync(a => a.Email.ToLower() == email);

            // Employee logins belong to an employee; the accountant and system owner do not.
            Employee? employee = null;
            if (account?.EmployeeId is int employeeId)
                employee = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

            // Same answer for "unknown email" and "wrong password", so nobody can
            // use the login form to find out which emails exist.
            if (account is null
                || !BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash)
                || (employee is not null && IsTerminated(employee)))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "Incorrect email or password.");
            }

            var name = employee?.Name ?? account.DisplayName ?? account.Email;
            var user = new UserDto(account.AccountId, account.EmployeeId, name, account.Email, account.Role);
            return Results.Ok(new LoginResponse(tokens.CreateToken(user), user));
        });

        // GET /api/auth/me   (needs a valid token) - handy for testing in Swagger
        group.MapGet("/me", (ClaimsPrincipal principal) =>
            {
                int? employeeId = int.TryParse(principal.FindFirstValue("employee_id"), out var id) ? id : null;

                return new UserDto(
                    int.Parse(principal.FindFirstValue("sub")!),
                    employeeId,
                    principal.FindFirstValue("name")!,
                    principal.FindFirstValue("email")!,
                    principal.FindFirstValue("role")!);
            })
            .RequireAuthorization();
    }

    private static bool IsTerminated(Employee e) =>
        e.TerminationDate is not null && e.TerminationDate < DateTime.Today;
}