using Infrastructure;
using Infrastructure.Entities;
using LinqToDB;
using LinqToDB.Async;

namespace Service;

/// <summary>
/// The life of a holiday request: preview, submit, withdraw (employee), and approve or decline (accountant).
/// Approving is the moment that holiday is actually taken from the balance: that is when the ledger rows are booked.
/// </summary>
public class HolidayRequestService(ExamDatabase db, RequestPlanner planner)
{
    // ------------------------------------------------------------------ employee

    public async Task<RequestPreviewDto> PreviewAsync(int employeeId, RequestInput input) =>
        (await planner.PlanAsync(employeeId, input, PlanMode.Submit)).Preview;

    public async Task<HolidayRequestDto> SubmitAsync(int employeeId, RequestInput input)
    {
        var plan = await planner.PlanAsync(employeeId, input, PlanMode.Submit);
        if (plan.Preview.Problems.Length > 0)
            throw new RuleViolationException(string.Join(" ", plan.Preview.Problems));

        var accountTypeId = await db.HolidayAccountTypes
            .Where(a => a.Code == input.Account)
            .Select(a => a.AccountTypeId)
            .FirstAsync();

        // Nothing is taken from the balance yet. That happens when the accountant approves.
        var request = new HolidayRequest
        {
            EmployeeId = employeeId,
            AccountTypeId = accountTypeId,
            StartDate = ToDateTime(input.StartDate),
            EndDate = ToDateTime(input.EndDate),
            IsMainHoliday = input.IsMainHoliday,
            Status = "SUBMITTED",
            TotalHours = plan.Preview.TotalHours,
            TotalDays = plan.Preview.TotalDays,
        };
        request.RequestId = await db.InsertWithInt32IdentityAsync(request);

        return new HolidayRequestDto(
            request.RequestId, input.StartDate, input.EndDate, input.Account,
            request.TotalHours, request.Status, request.IsMainHoliday);
    }

    /// <summary>Takes back a request that is still waiting for a decision.</summary>
    public async Task WithdrawAsync(int employeeId, int requestId)
    {
        var request = await db.HolidayRequests.FirstOrDefaultAsync(r => r.RequestId == requestId && r.EmployeeId == employeeId)
                      ?? throw new NotFoundException("Request not found.");

        if (request.Status != "SUBMITTED")
            throw new RuleViolationException(
                "Only requests that are waiting for a decision can be withdrawn. To cancel approved holiday, ask the accountant.");

        var changed = await db.HolidayRequests
            .Where(r => r.RequestId == requestId && r.Status == "SUBMITTED")
            .Set(r => r.Status, "CANCELLED_BY_EMPLOYEE")
            .UpdateAsync();

        if (changed == 0)
            throw new RuleViolationException("The request was decided in the meantime.");
    }

    // ------------------------------------------------------------------ accountant

    public async Task<List<PendingRequestDto>> ListPendingAsync()
    {
        var rows = await (
            from r in db.HolidayRequests
            join e in db.Employees on r.EmployeeId equals e.EmployeeId
            join a in db.HolidayAccountTypes on r.AccountTypeId equals a.AccountTypeId
            where r.Status == "SUBMITTED"
            orderby r.SubmittedAt
            select new
            {
                r.RequestId, r.EmployeeId, EmployeeName = e.Name, a.Code,
                r.StartDate, r.EndDate, r.IsMainHoliday, r.TotalHours, r.TotalDays, r.SubmittedAt,
            }
        ).ToListAsync();

        var result = new List<PendingRequestDto>();
        foreach (var r in rows)
        {
            // Would approving it work right now? The accountant sees the reasons next to the request.
            string[] problems;
            try
            {
                var input = new RequestInput(r.Code, ToDate(r.StartDate), ToDate(r.EndDate), r.IsMainHoliday);
                problems = (await planner.PlanAsync(r.EmployeeId, input, PlanMode.Approve, r.RequestId)).Preview.Problems;
            }
            catch (RuleViolationException ex)
            {
                problems = [ex.Message];
            }

            result.Add(new PendingRequestDto(
                r.RequestId, r.EmployeeId, r.EmployeeName, r.Code,
                ToDate(r.StartDate), ToDate(r.EndDate), r.IsMainHoliday,
                r.TotalHours, r.TotalDays, r.SubmittedAt, problems));
        }

        return result;
    }

    /// <summary>
    /// Re-checks everything with today's balance, then, in one transaction, marks the request approved,
    /// writes one row per calendar day, and books one USAGE row per holiday day in the ledger.
    /// </summary>
    public async Task<HolidayRequestDto> ApproveAsync(int requestId, int accountantAccountId, string? note)
    {
        var (request, account) = await LoadPendingAsync(requestId);

        var input = new RequestInput(account.Code, ToDate(request.StartDate), ToDate(request.EndDate), request.IsMainHoliday);
        var plan = await planner.PlanAsync(request.EmployeeId, input, PlanMode.Approve, requestId);
        if (plan.Preview.Problems.Length > 0)
            throw new RuleViolationException("This request cannot be approved: " + string.Join(" ", plan.Preview.Problems));

        var totalHours = plan.Preview.TotalHours;
        var totalDays = plan.Preview.TotalDays;
        var decidedAt = DateTime.UtcNow;
        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        await using var transaction = await db.BeginTransactionAsync();

        // Only a request that is still SUBMITTED can be approved (stops a double click from booking twice)
        var changed = await db.HolidayRequests
            .Where(r => r.RequestId == requestId && r.Status == "SUBMITTED")
            .Set(r => r.Status, "APPROVED")
            .Set(r => r.DecidedBy, (int?)accountantAccountId)
            .Set(r => r.DecidedAt, (DateTime?)decidedAt)
            .Set(r => r.DecisionNote, cleanNote)
            .Set(r => r.TotalHours, totalHours)
            .Set(r => r.TotalDays, totalDays)
            .UpdateAsync();
        if (changed == 0)
            throw new RuleViolationException("This request has already been decided.");

        var usageByDate = plan.Usages.ToDictionary(u => u.Date);

        foreach (var line in plan.Preview.Days)
        {
            await db.InsertAsync(new HolidayRequestDay
            {
                RequestId = requestId,
                Date = ToDateTime(line.Date),
                HoursDeducted = line.Hours,
                DaysDeducted = line.Days,
                BucketId = usageByDate.TryGetValue(line.Date, out var usage) ? usage.BucketId : null,
            });
        }

        foreach (var usage in plan.Usages)
        {
            await db.InsertAsync(new LedgerEntry
            {
                BucketId = usage.BucketId,
                EntryType = "USAGE",
                EntryDate = ToDateTime(usage.Date),
                Hours = -usage.Hours,
                Days = -usage.Days,
                RequestId = requestId,
                CreatedBy = accountantAccountId,
            });
        }

        await transaction.CommitAsync();

        return new HolidayRequestDto(
            requestId, input.StartDate, input.EndDate, account.Code, totalHours, "APPROVED", request.IsMainHoliday);
    }

    public async Task<HolidayRequestDto> DeclineAsync(int requestId, int accountantAccountId, string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new RuleViolationException("Please give a reason for declining.");

        var (request, account) = await LoadPendingAsync(requestId);

        var decidedAt = DateTime.UtcNow;
        var cleanNote = note.Trim();

        var changed = await db.HolidayRequests
            .Where(r => r.RequestId == requestId && r.Status == "SUBMITTED")
            .Set(r => r.Status, "REJECTED")
            .Set(r => r.DecidedBy, (int?)accountantAccountId)
            .Set(r => r.DecidedAt, (DateTime?)decidedAt)
            .Set(r => r.DecisionNote, cleanNote)
            .UpdateAsync();
        if (changed == 0)
            throw new RuleViolationException("This request has already been decided.");

        return new HolidayRequestDto(
            requestId, ToDate(request.StartDate), ToDate(request.EndDate), account.Code,
            request.TotalHours, "REJECTED", request.IsMainHoliday);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(HolidayRequest Request, HolidayAccountType Account)> LoadPendingAsync(int requestId)
    {
        var request = await db.HolidayRequests.FirstOrDefaultAsync(r => r.RequestId == requestId)
                      ?? throw new NotFoundException("Request not found.");

        if (request.Status != "SUBMITTED")
            throw new RuleViolationException("This request has already been decided.");

        var account = await db.HolidayAccountTypes.FirstAsync(a => a.AccountTypeId == request.AccountTypeId);
        return (request, account);
    }

    private static DateOnly ToDate(DateTime value) => DateOnly.FromDateTime(value);

    private static DateTime ToDateTime(DateOnly value) => value.ToDateTime(TimeOnly.MinValue);
}