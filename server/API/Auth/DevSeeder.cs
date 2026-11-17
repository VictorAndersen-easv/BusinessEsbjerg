using Infrastructure;
using Infrastructure.Entities;
using LinqToDB;
using LinqToDB.Async;

namespace api.Auth;

/// <summary>
/// DEVELOPMENT ONLY: creates three test users (password "demo") if they don't exist yet.
/// Passwords must be hashed by code, so we can't just put them in a .sql file.
/// </summary>
public static class DevSeeder
{
    public static async Task SeedAsync(ExamDatabase db)
    {
        var testUsers = new[]
        {
            (No: "E001", Name: "Anna",       Email: "anna@test.dk",       Role: Roles.Employee),
            (No: "E002", Name: "Accounting", Email: "accounting@test.dk", Role: Roles.Accounting),
            (No: "E003", Name: "Admin",      Email: "admin@test.dk",      Role: Roles.Admin),
        };

        foreach (var u in testUsers)
        {
            var email = u.Email;
            if (await db.Employees.AnyAsync(e => e.Email.ToLower() == email)) continue;

            await db.InsertAsync(new Employee
            {
                EmployeeNo = u.No,
                Name = u.Name,
                Email = u.Email,
                Role = u.Role,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo"),
                HireDate = DateTime.Today,
            });
        }
    }
}