namespace Service;

/// <summary>
/// What we send to the frontend for one request. The property names become the
/// JSON names (requestId, startDate, ...) and match HolidayRequest in Types.ts.
/// DateOnly is sent as "2027-02-15".
/// </summary>
public record HolidayRequestDto(
    int RequestId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Account,
    decimal TotalHours,
    string Status,
    bool IsMainHoliday);