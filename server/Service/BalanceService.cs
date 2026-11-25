using Infrastructure;
using LinqToDB;
using LinqToDB.Async;

namespace Service;

/// <summary>Turns the ledger into balances. Nothing is stored: every balance is a SUM of ledger rows.</summary>
public class BalanceService(ExamDatabase db)
{
    public async Task<List<AccountBalanceDto>> GetForEmployeeAsync(int employeeId, DateOnly today)
    {
        if (!await db.Employees.AnyAsync(e => e.EmployeeId == employeeId))
            throw new NotFoundException("Employee not found.");

        var accounts = await db.HolidayAccountTypes.OrderBy(a => a.AccountTypeId).ToListAsync();
        var buckets = await db.HolidayBuckets
            .Where(b => b.EmployeeId == employeeId && b.Status == "OPEN")
            .ToListAsync();

        var bucketIds = buckets.Select(b => b.BucketId).ToList();
        var entries = await db.LedgerEntries.Where(l => bucketIds.Contains(l.BucketId)).ToListAsync();

        return accounts
            .Select(account =>
            {
                var bucketDtos = buckets
                    .Where(b => b.AccountTypeId == account.AccountTypeId)
                    .OrderBy(b => b.EarningStart)
                    .Select(b =>
                    {
                        var rows = entries.Where(e => e.BucketId == b.BucketId).ToList();
                        var earned = rows.Where(r => r.EntryType == "ACCRUAL").ToList();

                        return new BucketBalanceDto(
                            b.BucketId,
                            DateOnly.FromDateTime(b.EarningStart),
                            DateOnly.FromDateTime(b.EarningEnd),
                            DateOnly.FromDateTime(b.UsageStart),
                            DateOnly.FromDateTime(b.UsageEnd),
                            earned.Sum(r => r.Hours),
                            earned.Sum(r => r.Days),
                            rows.Sum(r => r.Hours),
                            rows.Sum(r => r.Days));
                    })
                    .ToArray();

                var usableNow = bucketDtos.Where(d => d.UsageStart <= today && today <= d.UsageEnd).ToList();

                return new AccountBalanceDto(
                    account.Code,
                    account.Name,
                    usableNow.Sum(d => d.RemainingHours),
                    usableNow.Sum(d => d.RemainingDays),
                    bucketDtos.Sum(d => d.RemainingHours),
                    bucketDtos.Sum(d => d.RemainingDays),
                    bucketDtos);
            })
            .Where(a => a.Buckets.Length > 0) // an account that is switched off has no buckets
            .ToList();
    }
}