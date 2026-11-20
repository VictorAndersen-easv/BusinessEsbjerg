using Infrastructure;
using Infrastructure.Entities;
using LinqToDB;
using LinqToDB.Async;

namespace api.Auth;

/// <summary>
/// DEVELOPMENT ONLY: creates test logins (password "demo"), two employees with working
/// schedules, and a few sample holiday requests, but only if they don't exist yet.
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

        await SeedRequestsAsync(db);
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

    private static async Task SeedRequestsAsync(ExamDatabase db)
    {
        var annaLogin = await db.UserAccounts.FirstAsync(a => a.Email.ToLower() == "anna" + Domain);
        var annaId = annaLogin.EmployeeId!.Value;
        if (await db.HolidayRequests.AnyAsync(r => r.EmployeeId == annaId)) return;

        var accountant = await db.UserAccounts.FirstAsync(a => a.Role == Roles.Accountant);

        var accountIds = await db.HolidayAccountTypes.ToDictionaryAsync(a => a.Code, a => a.AccountTypeId);
        if (!accountIds.TryGetValue("WEEKS_1_5", out var weeks15) || !accountIds.TryGetValue("WEEK_6", out var week6))
            throw new InvalidOperationException(
                "holiday_account_type is empty. Run the SQL schema script (it seeds the two account types) first.");

        // Hours follow the schedule: Mon-Thu 7.5 h, Fri 7 h, so one full week = 37 h.
        var samples = new[]
        {
            (Start: new DateTime(2027, 2, 15),  End: new DateTime(2027, 2, 19),  Account: weeks15, Hours: 37m,  Days: 5m,  Status: "CANCELLATION_REQUESTED", Main: false),
            (Start: new DateTime(2027, 7, 5),   End: new DateTime(2027, 7, 23),  Account: weeks15, Hours: 111m, Days: 15m, Status: "SUBMITTED",              Main: true),
            (Start: new DateTime(2026, 10, 23), End: new DateTime(2026, 10, 23), Account: weeks15, Hours: 7m,   Days: 1m,  Status: "APPROVED",               Main: false),
            (Start: new DateTime(2027, 5, 10),  End: new DateTime(2027, 5, 14),  Account: week6,   Hours: 37m,  Days: 5m,  Status: "REJECTED",               Main: false),
            (Start: new DateTime(2027, 3, 8),   End: new DateTime(2027, 3, 9),   Account: weeks15, Hours: 15m,  Days: 2m,  Status: "CANCELLED_BY_EMPLOYEE",  Main: false),
        };

        foreach (var s in samples)
        {
            var decided = s.Status is "APPROVED" or "REJECTED" or "CANCELLATION_REQUESTED";
            var cancelRequested = s.Status == "CANCELLATION_REQUESTED";

            await db.InsertAsync(new HolidayRequest
            {
                EmployeeId = annaId,
                AccountTypeId = s.Account,
                StartDate = s.Start,
                EndDate = s.End,
                IsMainHoliday = s.Main,
                Status = s.Status,
                TotalHours = s.Hours,
                TotalDays = s.Days,
                DecidedBy = decided ? accountant.AccountId : null,
                DecidedAt = decided ? DateTime.UtcNow : null,
                CancellationRequestedAt = cancelRequested ? DateTime.UtcNow : null,
                CancelReason = cancelRequested ? "My plans changed." : null,
            });
        }
    }
}