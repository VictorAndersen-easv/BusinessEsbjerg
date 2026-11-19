using Infrastructure;
using Infrastructure.Entities;
using LinqToDB;
using LinqToDB.Async;

namespace api.Auth;

/// <summary>
/// DEVELOPMENT ONLY: creates test users (password "demo") and a few sample holiday
/// requests for Anna, but only if they don't exist yet.
/// Passwords must be hashed by code, so we can't just put them in a .sql file.
/// </summary>
public static class DevSeeder
{
    public static async Task SeedAsync(ExamDatabase db)
    {
        await SeedUsersAsync(db);
        await SeedRequestsAsync(db);
    }

    private static async Task SeedUsersAsync(ExamDatabase db)
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

    private static async Task SeedRequestsAsync(ExamDatabase db)
    {
        var anna = await db.Employees.FirstAsync(e => e.Email.ToLower() == "anna@test.dk");
        if (await db.HolidayRequests.AnyAsync(r => r.EmployeeId == anna.EmployeeId)) return;

        var accounting = await db.Employees.FirstAsync(e => e.Email.ToLower() == "accounting@test.dk");

        var accountIds = await db.HolidayAccountTypes.ToDictionaryAsync(a => a.Code, a => a.AccountTypeId);
        if (!accountIds.TryGetValue("WEEKS_1_5", out var weeks15) || !accountIds.TryGetValue("WEEK_6", out var week6))
            throw new InvalidOperationException(
                "holiday_account_type is empty. Run the SQL schema script (it seeds the two account types) first.");

        // Hours follow the company schedule: Mon-Thu 7.5 h, Fri 7 h, so one full week = 37 h.
        var samples = new[]
        {
            (Start: new DateTime(2027, 2, 15), End: new DateTime(2027, 2, 19), Account: weeks15, Hours: 37m,  Status: "APPROVED",              Main: false),
            (Start: new DateTime(2027, 7, 5),  End: new DateTime(2027, 7, 23), Account: weeks15, Hours: 111m, Status: "SUBMITTED",             Main: true),
            (Start: new DateTime(2026, 10, 23), End: new DateTime(2026, 10, 23), Account: weeks15, Hours: 7m, Status: "APPROVED",             Main: false),
            (Start: new DateTime(2027, 5, 10), End: new DateTime(2027, 5, 14), Account: week6,   Hours: 37m,  Status: "REJECTED",              Main: false),
            (Start: new DateTime(2027, 3, 8),  End: new DateTime(2027, 3, 9),  Account: weeks15, Hours: 15m,  Status: "CANCELLED_BY_EMPLOYEE", Main: false),
        };

        foreach (var s in samples)
        {
            var decided = s.Status is "APPROVED" or "REJECTED";

            await db.InsertAsync(new HolidayRequest
            {
                EmployeeId = anna.EmployeeId,
                AccountTypeId = s.Account,
                StartDate = s.Start,
                EndDate = s.End,
                IsMainHoliday = s.Main,
                Status = s.Status,
                TotalHours = s.Hours,
                DecidedBy = decided ? accounting.EmployeeId : null,
                DecidedAt = decided ? DateTime.UtcNow : null,
            });
        }
    }
}