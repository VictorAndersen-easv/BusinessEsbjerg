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
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Email.ToLower() == email);

            // Same answer for "unknown email" and "wrong password", so nobody can
            // use the login form to find out which emails exist.
            if (employee is null
                || employee.PasswordHash is null
                || IsTerminated(employee)
                || !BCrypt.Net.BCrypt.Verify(request.Password, employee.PasswordHash))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "Incorrect email or password.");
            }

            var user = new UserDto(employee.EmployeeId, employee.Name, employee.Email, employee.Role);
            return Results.Ok(new LoginResponse(tokens.CreateToken(employee), user));
        });

        // GET /api/auth/me   (needs a valid token) - handy for testing in Swagger
        group.MapGet("/me", (ClaimsPrincipal principal) => new UserDto(
                int.Parse(principal.FindFirstValue("sub")!),
                principal.FindFirstValue("name")!,
                principal.FindFirstValue("email")!,
                principal.FindFirstValue("role")!))
            .RequireAuthorization();
    }

    private static bool IsTerminated(Employee e) =>
        e.TerminationDate is not null && e.TerminationDate < DateTime.Today;
}