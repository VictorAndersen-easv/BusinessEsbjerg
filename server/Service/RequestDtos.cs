namespace Service;

// ---- What the frontend SENDS ----

/// <summary>Account is "WEEKS_1_5" or "WEEK_6". Holiday is requested in whole days.</summary>
public record RequestInput(string Account, DateOnly StartDate, DateOnly EndDate, bool IsMainHoliday);

/// <summary>The accountant's note. Required when declining.</summary>
public record DecisionInput(string? Note);

// ---- What the frontend RECEIVES ----

/// <summary>
/// One calendar day of a request. Kind is HOLIDAY, WEEKEND, PUBLIC_HOLIDAY, COMPANY_DAY_OFF or NOT_WORKING
/// (a weekday the employee does not work). Only HOLIDAY days cost anything.
/// </summary>
public record PlannedDayDto(DateOnly Date, string Kind, decimal Hours, decimal Days, string? Note);

/// <summary>What a request would cost and whether it is allowed. Problems block it; warnings only advise.</summary>
public record RequestPreviewDto(
    string Account,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalHours,
    decimal TotalDays,
    PlannedDayDto[] Days,
    string[] Problems,
    string[] Warnings,
    bool CanSubmit);

/// <summary>A request waiting for the accountant. Problems lists why it could not be approved right now.</summary>
public record PendingRequestDto(
    int RequestId,
    int EmployeeId,
    string EmployeeName,
    string Account,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsMainHoliday,
    decimal TotalHours,
    decimal TotalDays,
    DateTime SubmittedAt,
    string[] Problems);