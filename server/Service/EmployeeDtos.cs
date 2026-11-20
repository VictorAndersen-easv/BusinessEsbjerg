namespace Service;

// ---- What the frontend SENDS ----

/// <summary>One weekday of a schedule. Times look like "08:00". Leave both empty for a day off.</summary>
public record ScheduleDayInput(string? Start, string? End);

/// <summary>Days must be exactly five entries: Monday, Tuesday, Wednesday, Thursday, Friday.</summary>
public record CreateEmployeeRequest(
    string Name,
    string Email,
    string TemporaryPassword,
    DateOnly HireDate,
    ScheduleDayInput[] Days);

/// <summary>StartsOn must be a Monday.</summary>
public record ChangeScheduleRequest(DateOnly StartsOn, ScheduleDayInput[] Days);

// ---- What the frontend RECEIVES ----

public record EmployeeDto(
    int EmployeeId,
    string EmployeeNo,
    string Name,
    string Email,
    DateOnly HireDate,
    DateOnly? TerminationDate,
    decimal? CurrentWeeklyHours);

public record ScheduleDayDto(int Weekday, string? Start, string? End, decimal Hours);

/// <summary>ValidTo is null while the schedule is still in use.</summary>
public record ScheduleDto(int ScheduleId, DateOnly ValidFrom, DateOnly? ValidTo, decimal WeeklyHours, ScheduleDayDto[] Days);