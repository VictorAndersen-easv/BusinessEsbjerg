using System.Globalization;
using Infrastructure;
using Infrastructure.Entities;
using LinqToDB;
using LinqToDB.Async;

namespace Service;

/// <summary>Creating employees and managing their weekly working schedules.</summary>
public class EmployeeService(ExamDatabase db, EmployeeSettings settings, ScheduleConversionService conversions)
{
    private static readonly string[] WeekdayNames = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];

    // ------------------------------------------------------------------ reading

    public async Task<List<EmployeeDto>> ListAsync()
    {
        var today = DateTime.Today;

        var employees = await db.Employees.OrderBy(e => e.Name).ToListAsync();
        var logins = await db.UserAccounts.Where(a => a.EmployeeId != null).ToListAsync();
        var current = await db.WorkSchedules
            .Where(s => s.ValidFrom <= today && (s.ValidTo == null || s.ValidTo >= today))
            .ToListAsync();

        var currentIds = current.Select(s => s.ScheduleId).ToList();
        var days = await db.WorkScheduleDays.Where(d => currentIds.Contains(d.ScheduleId)).ToListAsync();

        return employees.Select(e =>
        {
            var login = logins.FirstOrDefault(a => a.EmployeeId == e.EmployeeId);
            var schedule = current.FirstOrDefault(s => s.EmployeeId == e.EmployeeId);
            decimal? weeklyHours = schedule is null
                ? null
                : days.Where(d => d.ScheduleId == schedule.ScheduleId).Sum(d => d.Hours);

            return new EmployeeDto(
                e.EmployeeId, e.EmployeeNo, e.Name, login?.Email ?? "",
                ToDate(e.HireDate), e.TerminationDate is null ? null : ToDate(e.TerminationDate.Value),
                weeklyHours);
        }).ToList();
    }

    /// <summary>The full schedule history of one employee, newest first.</summary>
    public async Task<List<ScheduleDto>> GetSchedulesAsync(int employeeId)
    {
        await GetEmployeeAsync(employeeId);

        var schedules = await db.WorkSchedules
            .Where(s => s.EmployeeId == employeeId)
            .OrderByDescending(s => s.ValidFrom)
            .ToListAsync();

        var ids = schedules.Select(s => s.ScheduleId).ToList();
        var days = await db.WorkScheduleDays.Where(d => ids.Contains(d.ScheduleId)).ToListAsync();

        return schedules
            .Select(s => ToDto(s, days.Where(d => d.ScheduleId == s.ScheduleId)))
            .ToList();
    }

    // ------------------------------------------------------------------ creating

    /// <summary>Creates the employee, their login and their first schedule. All three, or nothing.</summary>
    public async Task<EmployeeDto> CreateAsync(CreateEmployeeRequest request, int actingAccountId)
    {
        var name = (request.Name ?? "").Trim();
        if (name.Length is 0 or > 150)
            throw new RuleViolationException("Name is required (at most 150 characters).");

        var email = ValidateEmail(request.Email);

        var password = request.TemporaryPassword ?? "";
        if (password.Length < 8)
            throw new RuleViolationException("The temporary password must be at least 8 characters.");
        if (password.Length > 72)
            throw new RuleViolationException("The password can be at most 72 characters.");

        if (request.HireDate == default)
            throw new RuleViolationException("Hire date is required.");

        var days = ParseSchedule(request.Days);

        if (await db.UserAccounts.AnyAsync(a => a.Email.ToLower() == email))
            throw new RuleViolationException("There is already a login with that email.");

        var employeeNo = await NextEmployeeNoAsync();

        await using var transaction = await db.BeginTransactionAsync();

        var employeeId = await db.InsertWithInt32IdentityAsync(new Employee
        {
            EmployeeNo = employeeNo,
            Name = name,
            HireDate = ToDateTime(request.HireDate),
        });

        await db.InsertAsync(new UserAccount
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = Roles.Employee,
            EmployeeId = employeeId,
        });

        // Schedules always start on a Monday: use the Monday of the week the employee starts.
        await InsertScheduleAsync(employeeId, MondayOnOrBefore(request.HireDate), days, actingAccountId);

        await transaction.CommitAsync();

        return new EmployeeDto(employeeId, employeeNo, name, email, request.HireDate, null, days.Sum(d => d.Hours));
    }

    // ------------------------------------------------------------------ changing a schedule

    /// <summary>
    /// Closes the current schedule the day before <see cref="ChangeScheduleRequest.StartsOn"/> and adds a new one.
    /// Nothing is edited in place, so the full history is kept.
    /// </summary>
    public async Task<ScheduleDto> ChangeScheduleAsync(int employeeId, ChangeScheduleRequest request, int actingAccountId)
    {
        var employee = await GetEmployeeAsync(employeeId);
        var days = ParseSchedule(request.Days);

        if (request.StartsOn.DayOfWeek != DayOfWeek.Monday)
            throw new RuleViolationException("A schedule must start on a Monday.");

        // Months before this one are already booked in the (unchangeable) holiday ledger with the old schedule
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (request.StartsOn < new DateOnly(today.Year, today.Month, 1))
            throw new RuleViolationException(
                "A schedule cannot start before the current month, because earlier months are already booked in the holiday ledger.");

        if (employee.TerminationDate is { } left && ToDate(left) < request.StartsOn)
            throw new RuleViolationException("The employee has already left before that date.");

        var latest = await db.WorkSchedules
            .Where(s => s.EmployeeId == employeeId)
            .OrderByDescending(s => s.ValidFrom)
            .FirstOrDefaultAsync()
            ?? throw new RuleViolationException("The employee has no schedule to change.");

        if (latest.ValidTo is not null)
            throw new RuleViolationException("The latest schedule already has an end date.");

        if (request.StartsOn <= ToDate(latest.ValidFrom))
            throw new RuleViolationException(
                $"The new schedule must start after the current one, which starts on {ToDate(latest.ValidFrom):yyyy-MM-dd}.");

        await using var transaction = await db.BeginTransactionAsync();

        // Close the old schedule on the Sunday before the new one starts
        var closesOn = ToDateTime(request.StartsOn.AddDays(-1));
        await db.WorkSchedules
            .Where(s => s.ScheduleId == latest.ScheduleId)
            .Set(s => s.ValidTo, (DateTime?)closesOn)
            .UpdateAsync();

        var newScheduleId = await InsertScheduleAsync(employeeId, request.StartsOn, days, actingAccountId);

        // The days stay the same and the hours follow the new day length (see ScheduleConversionService).
        // A schedule that starts later is converted by the daily job on the day it starts.
        if (request.StartsOn <= today)
            await conversions.ConvertDueAsync(employeeId, today, actingAccountId);

        await transaction.CommitAsync();

        return (await GetSchedulesAsync(employeeId)).First(s => s.ScheduleId == newScheduleId);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Employee> GetEmployeeAsync(int employeeId) =>
        await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId)
        ?? throw new NotFoundException("Employee not found.");

    private string ValidateEmail(string? raw)
    {
        var email = (raw ?? "").Trim().ToLowerInvariant();
        var domain = settings.EmailDomain.ToLowerInvariant();

        if (!email.EndsWith(domain, StringComparison.Ordinal))
            throw new RuleViolationException($"The email must end with {domain}.");

        var local = email[..^domain.Length];
        if (local.Length == 0 || local.Any(c => c == '@' || char.IsWhiteSpace(c)))
            throw new RuleViolationException($"The email must look like name{domain}.");

        return email;
    }

    private async Task<string> NextEmployeeNoAsync()
    {
        var numbers = await db.Employees.Select(e => e.EmployeeNo).ToListAsync();
        var highest = numbers
            .Select(n => int.TryParse(n.TrimStart('E'), out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"E{highest + 1:000}";
    }

    private async Task<int> InsertScheduleAsync(int employeeId, DateOnly validFrom, List<ParsedDay> days, int actingAccountId)
    {
        var scheduleId = await db.InsertWithInt32IdentityAsync(new WorkSchedule
        {
            EmployeeId = employeeId,
            ValidFrom = ToDateTime(validFrom),
            CreatedBy = actingAccountId,
        });

        foreach (var day in days)
        {
            await db.InsertAsync(new WorkScheduleDay
            {
                ScheduleId = scheduleId,
                Weekday = day.Weekday,
                StartTime = day.Start,
                EndTime = day.End,
                Hours = day.Hours,
            });
        }

        return scheduleId;
    }

    private sealed record ParsedDay(short Weekday, TimeSpan? Start, TimeSpan? End, decimal Hours);

    /// <summary>Checks the five days and works out the hours of each (end minus start, two decimals).</summary>
    private static List<ParsedDay> ParseSchedule(ScheduleDayInput[]? input)
    {
        if (input is null || input.Length != 5)
            throw new RuleViolationException("A schedule needs exactly 5 days: Monday to Friday.");

        var result = new List<ParsedDay>();

        for (var i = 0; i < 5; i++)
        {
            var day = WeekdayNames[i];
            var hasStart = !string.IsNullOrWhiteSpace(input[i].Start);
            var hasEnd = !string.IsNullOrWhiteSpace(input[i].End);

            if (!hasStart && !hasEnd)
            {
                result.Add(new ParsedDay((short)(i + 1), null, null, 0m));
                continue;
            }

            if (hasStart != hasEnd)
                throw new RuleViolationException($"{day}: give both a start and an end time, or leave both empty for a day off.");

            if (!TimeOnly.TryParseExact(input[i].Start!.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !TimeOnly.TryParseExact(input[i].End!.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                throw new RuleViolationException($"{day}: times must look like 08:00.");

            if (end <= start)
                throw new RuleViolationException($"{day}: the end time must be after the start time.");

            var hours = Math.Round((decimal)(end - start).TotalHours, 2, MidpointRounding.AwayFromZero);
            result.Add(new ParsedDay((short)(i + 1), start.ToTimeSpan(), end.ToTimeSpan(), hours));
        }

        if (result.All(d => d.Hours == 0))
            throw new RuleViolationException("A schedule needs at least one working day.");

        return result;
    }

    private static ScheduleDto ToDto(WorkSchedule schedule, IEnumerable<WorkScheduleDay> days)
    {
        var dayDtos = days
            .OrderBy(d => d.Weekday)
            .Select(d => new ScheduleDayDto(d.Weekday, d.StartTime?.ToString(@"hh\:mm"), d.EndTime?.ToString(@"hh\:mm"), d.Hours))
            .ToArray();

        return new ScheduleDto(
            schedule.ScheduleId,
            ToDate(schedule.ValidFrom),
            schedule.ValidTo is null ? null : ToDate(schedule.ValidTo.Value),
            dayDtos.Sum(d => d.Hours),
            dayDtos);
    }

    private static DateOnly MondayOnOrBefore(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static DateOnly ToDate(DateTime value) => DateOnly.FromDateTime(value);

    private static DateTime ToDateTime(DateOnly value) => value.ToDateTime(TimeOnly.MinValue);
}