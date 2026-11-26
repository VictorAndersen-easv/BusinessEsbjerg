using Infrastructure;
using Infrastructure.Entities;
using LinqToDB;
using LinqToDB.Async;

namespace api.Auth;

/// <summary>
/// DEVELOPMENT ONLY: creates test logins (password "demo"), two employees with working
/// schedules, but only if they don't exist yet. (Holiday requests are made through the app.)
/// Passwords must be hashed by code, so we can't just put them in a .sql file.
/// </summary>
public static class DevSeeder
{
    private const string Domain = "@businessesbjerg.com";

    // Monday-Friday as (start, end). (null, null) = day off.
    private static readonly (TimeSpan? Start, TimeSpan? End)[] FullTime =
    [
        (Time(8, 0), Time(15, 30)),   // Monday    7.5 h
        (Time(8, 0), Time(15, 30)),   // Tuesday   7.5 h
        (Time(8, 0), Time(15, 30)),   // Wednesday 7.5 h
        (Time(8, 0), Time(15, 30)),   // Thursday  7.5 h
        (Time(8, 0), Time(15, 0)),    // Friday    7 h   => 37 h per week
    ];

    private static readonly (TimeSpan? Start, TimeSpan? End)[] PartTime =
    [
        (Time(8, 0), Time(15, 30)),   // Monday
        (Time(8, 0), Time(15, 30)),   // Tuesday
        (Time(8, 0), Time(15, 30)),   // Wednesday
        (null, null),                 // Thursday off
        (null, null),                 // Friday off   => 22.5 h per week
    ];

    private static TimeSpan Time(int hours, int minutes) => new(hours, minutes, 0);

    public static async Task SeedAsync(ExamDatabase db)
    {
        await EnsureRoleLoginAsync(db, "accountant" + Domain, "Accountant", Roles.Accountant);
        await EnsureRoleLoginAsync(db, "owner" + Domain, "System owner", Roles.SystemOwner);

        await EnsureEmployeeAsync(db, "E001", "Anna Hansen", "anna" + Domain, FullTime);
        await EnsureEmployeeAsync(db, "E002", "Clara Nielsen", "clara" + Domain, PartTime);

    }

    /// <summary>The accountant and system owner logins: no employee behind them.</summary>
    private static async Task EnsureRoleLoginAsync(ExamDatabase db, string email, string displayName, string role)
    {
        if (await db.UserAccounts.AnyAsync(a => a.Email.ToLower() == email)) return;

        await db.InsertAsync(new UserAccount
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo"),
            Role = role,
            DisplayName = displayName,
        });
    }

    /// <summary>An employee, their login and their first working schedule, all or nothing.</summary>
    private static async Task EnsureEmployeeAsync(
        ExamDatabase db, string employeeNo, string name, string email, (TimeSpan? Start, TimeSpan? End)[] week)
    {
        if (await db.UserAccounts.AnyAsync(a => a.Email.ToLower() == email)) return;

        var hireDate = new DateTime(2025, 9, 1); // a Monday, so the schedule can start the same day

        await using var transaction = await db.BeginTransactionAsync();

        var employeeId = await db.InsertWithInt32IdentityAsync(new Employee
        {
            EmployeeNo = employeeNo,
            Name = name,
            HireDate = hireDate,
        });

        await db.InsertAsync(new UserAccount
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo"),
            Role = Roles.Employee,
            EmployeeId = employeeId,
        });

        var scheduleId = await db.InsertWithInt32IdentityAsync(new WorkSchedule
        {
            EmployeeId = employeeId,
            ValidFrom = hireDate,
        });

        for (var i = 0; i < week.Length; i++)
        {
            var (start, end) = week[i];
            var hours = start is null || end is null
                ? 0m
                : Math.Round((decimal)(end.Value - start.Value).TotalHours, 2);

            await db.InsertAsync(new WorkScheduleDay
            {
                ScheduleId = scheduleId,
                Weekday = (short)(i + 1),
                StartTime = start,
                EndTime = end,
                Hours = hours,
            });
        }

        await transaction.CommitAsync();
    }
}