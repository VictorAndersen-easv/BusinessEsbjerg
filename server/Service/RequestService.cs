using Infrastructure;
using LinqToDB;
using LinqToDB.Async;

namespace Service;

/// <summary>Business logic around holiday requests. The endpoint calls this; this talks to the database.</summary>
public class RequestService(ExamDatabase db)
{
    public async Task<List<HolidayRequestDto>> GetForEmployeeAsync(int employeeId)
    {
        // This LINQ becomes one SQL query: SELECT ... FROM holiday_request JOIN holiday_account_type ...
        var rows = await (
            from r in db.HolidayRequests
            join a in db.HolidayAccountTypes on r.AccountTypeId equals a.AccountTypeId
            where r.EmployeeId == employeeId
            orderby r.StartDate descending
            select new
            {
                r.RequestId,
                r.StartDate,
                r.EndDate,
                a.Code,
                r.TotalHours,
                r.Status,
                r.IsMainHoliday,
            }
        ).ToListAsync();

        // The database did the filtering and sorting. Turning DateTime into DateOnly happens here in C#.
        return rows
            .Select(x => new HolidayRequestDto(
                x.RequestId,
                DateOnly.FromDateTime(x.StartDate),
                DateOnly.FromDateTime(x.EndDate),
                x.Code,
                x.TotalHours,
                x.Status,
                x.IsMainHoliday))
            .ToList();
    }
}