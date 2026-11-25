using Infrastructure;
using Infrastructure.Entities;
using LinqToDB;
using LinqToDB.Async;

namespace Service;

public record AccrualResult(int EmployeesProcessed, int BucketsCreated, int EntriesCreated);

/// <summary>
/// Creates the holiday buckets and books the monthly ACCRUAL rows in the ledger.
/// It is safe to run as often as you like: months that are already in the ledger are skipped,
/// and months that were missed (the app was off) are caught up.
/// </summary>
public class AccrualService(ExamDatabase db, ScheduleConversionService conversions)
{
    private const string Weeks15 = "WEEKS_1_5";
    private const string Week6 = "WEEK_6";

    // Only one accrual run at a time (the daily job and a manual run must not collide)
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <param name="asOf">Months that ended before this date are booked. Normally today.</param>
    public async Task<AccrualResult> RunAsync(DateOnly asOf)
    {
        await Gate.WaitAsync();
        try
        {
            return await RunLockedAsync(asOf);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<AccrualResult> RunLockedAsync(DateOnly asOf)
    {
        var rules = await RuleBook.LoadAsync(db);
        var accountTypes = await db.HolidayAccountTypes.ToListAsync();
        var employees = await db.Employees.ToListAsync();
        var allSchedules = await db.WorkSchedules.ToListAsync();
        var allDays = await db.WorkScheduleDays.ToListAsync();

        var processed = 0;
        var bucketsCreated = 0;
        var entriesCreated = 0;

        foreach (var employee in employees)
        {
            var mine = allSchedules.Where(s => s.EmployeeId == employee.EmployeeId).OrderBy(s => s.ValidFrom).ToList();
            if (mine.Count == 0) continue; // no schedule, no way to know the hours

            var schedules = new ScheduleBook(mine, allDays);

            // All of one employee's accruals succeed together or not at all
            await using var transaction = await db.BeginTransactionAsync();

            foreach (var account in accountTypes)
            {
                if (account.Code == Week6 && !rules.Bool("W6_ENABLED", asOf)) continue;

                var (newBuckets, newEntries) = await AccrueAccountAsync(employee, account, schedules, rules, asOf);
                bucketsCreated += newBuckets;
                entriesCreated += newEntries;
            }

            // Schedules that have started since the last run: keep the days, recalculate the hours
            await conversions.ConvertDueAsync(employee.EmployeeId, asOf, null);

            await transaction.CommitAsync();
            processed++;
        }

        return new AccrualResult(processed, bucketsCreated, entriesCreated);
    }

    /// <summary>All earning periods of one account for one employee, from hire date up to asOf.</summary>
    private async Task<(int Buckets, int Entries)> AccrueAccountAsync(
        Employee employee, HolidayAccountType account, ScheduleBook schedules, RuleBook rules, DateOnly asOf)
    {
        var hire = ToDate(employee.HireDate);
        DateOnly? left = employee.TerminationDate is null ? null : ToDate(employee.TerminationDate.Value);

        var lastDay = left is { } leaving && leaving < asOf ? leaving : asOf;
        if (lastDay < hire) return (0, 0);

        var startKey = account.Code == Week6 ? "W6_EARNING_START" : "W15_EARNING_START";
        var (startMonth, startDay) = rules.MonthDay(startKey, asOf);

        var newBuckets = 0;
        var newEntries = 0;

        for (var periodStart = PeriodStartContaining(hire, startMonth, startDay);
             periodStart <= lastDay;
             periodStart = periodStart.AddYears(1))
        {
            var periodEnd = periodStart.AddYears(1).AddDays(-1);
            var earningStart = ToDateTime(periodStart);

            var bucket = await db.HolidayBuckets.FirstOrDefaultAsync(b =>
                b.EmployeeId == employee.EmployeeId
                && b.AccountTypeId == account.AccountTypeId
                && b.EarningStart == earningStart);

            if (bucket is null)
            {
                bucket = BuildBucket(employee.EmployeeId, account, periodStart, periodEnd, rules);
                bucket.BucketId = await db.InsertWithInt32IdentityAsync(bucket);
                newBuckets++;
            }

            newEntries += await AccrueBucketAsync(hire, left, account, bucket, periodStart, periodEnd, schedules, rules, asOf);
        }

        return (newBuckets, newEntries);
    }

    /// <summary>
    /// Books one ACCRUAL row for every finished month of the earning period that is not in the ledger yet.
    ///
    /// The rounding rule: we never round each month on its own (that drifts). Instead we keep the EXACT
    /// running total, round THAT to two decimals, and book the difference to what is already booked.
    /// </summary>
    private async Task<int> AccrueBucketAsync(
        DateOnly hire, DateOnly? left, HolidayAccountType account, HolidayBucket bucket,
        DateOnly periodStart, DateOnly periodEnd, ScheduleBook schedules, RuleBook rules, DateOnly asOf)
    {
        var booked = await db.LedgerEntries
            .Where(l => l.BucketId == bucket.BucketId && l.EntryType == "ACCRUAL")
            .ToListAsync();

        decimal exactDays = 0;   // running exact totals for this earning period
        decimal exactHours = 0;
        var created = 0;

        for (var monthStart = periodStart; monthStart <= periodEnd; monthStart = monthStart.AddMonths(1))
        {
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            if (monthEnd >= asOf) break; // this month is not over yet

            // How much of the month was the person employed?
            var employedFrom = monthStart > hire ? monthStart : hire;
            var employedTo = left is { } gone && gone < monthEnd ? gone : monthEnd;
            if (employedTo < employedFrom) continue;

            var employedDays = employedTo.DayNumber - employedFrom.DayNumber + 1;
            var wholeMonth = employedFrom == monthStart && employedTo == monthEnd;
            var divisor = rules.Decimal("PARTIAL_MONTH_DIVISOR", monthEnd, 30m);
            var share = wholeMonth ? 1m : Math.Min(1m, employedDays / divisor);

            // The schedule in force at the end of the employed part of the month
            var (weeklyHours, dayLength) = schedules.On(employedTo);

            decimal monthDays, monthHours;
            if (account.Code == Week6)
            {
                // Week 6 is kept in hours and grows in proportion to the working hours
                var fullTimeWeek = rules.Decimal("FULL_TIME_WEEK_HOURS", monthEnd);
                monthHours = rules.Decimal("W6_HOURS_PER_MONTH", monthEnd) * share * (weeklyHours / fullTimeWeek);
                monthDays = monthHours / dayLength;
            }
            else
            {
                // Weeks 1-5 are kept in days. A day is a day, whatever the working hours are.
                monthDays = rules.Decimal("W15_DAYS_PER_MONTH", monthEnd) * share;
                monthHours = monthDays * dayLength;
            }

            exactDays += monthDays;
            exactHours += monthHours;

            // At the very end of the period, weeks 1-5 are settled to whole days (24.96 becomes 25).
            // Only for people who were employed through the end of the period.
            if (monthEnd == periodEnd
                && account.Code == Weeks15
                && (left is null || left >= periodEnd)
                && rules.Bool("W15_ROUND_WHOLE_DAYS_AT_PERIOD_END", monthEnd, true))
            {
                var wholeDays = Math.Round(exactDays, 0, MidpointRounding.AwayFromZero);
                exactHours += (wholeDays - exactDays) * dayLength;
                exactDays = wholeDays;
            }

            // Already booked in an earlier run? Then leave it alone: history is not rewritten.
            if (booked.Any(b => ToDate(b.EntryDate) == monthEnd)) continue;

            var bookedDays = booked.Where(b => ToDate(b.EntryDate) <= monthEnd).Sum(b => b.Days);
            var bookedHours = booked.Where(b => ToDate(b.EntryDate) <= monthEnd).Sum(b => b.Hours);

            var hoursToBook = Round2(exactHours) - bookedHours;
            var daysToBook = Round2(exactDays) - bookedDays;
            if (hoursToBook <= 0) continue;

            var entry = new LedgerEntry
            {
                BucketId = bucket.BucketId,
                EntryType = "ACCRUAL",
                EntryDate = ToDateTime(monthEnd),
                Hours = hoursToBook,
                Days = daysToBook,
                Note = $"Accrual {monthStart:yyyy-MM}",
            };

            await db.InsertAsync(entry);
            booked.Add(entry);
            created++;
        }

        return created;
    }

    /// <summary>The usage window and transfer deadline of a bucket, all from the global rules.</summary>
    private static HolidayBucket BuildBucket(
        int employeeId, HolidayAccountType account, DateOnly start, DateOnly end, RuleBook rules)
    {
        DateOnly usageStart, usageEnd, transferDeadline;

        if (account.Code == Week6)
        {
            // Earned Jan-Dec year Y, usable 1 May Y+1 to 30 April Y+2
            var (usageStartMonth, usageStartDay) = rules.MonthDay("W6_USAGE_START", start);
            var (usageEndMonth, usageEndDay) = rules.MonthDay("W6_USAGE_END", start);
            var (transferMonth, transferDay) = rules.MonthDay("W6_TRANSFER_DEADLINE", start);

            usageStart = new DateOnly(end.Year + 1, usageStartMonth, usageStartDay);
            usageEnd = new DateOnly(end.Year + 2, usageEndMonth, usageEndDay);
            transferDeadline = new DateOnly(end.Year + 1, transferMonth, transferDay);
        }
        else
        {
            // Earned Sep-Aug, usable from the start of earning until 16 months after the earning period ends
            var (transferMonth, transferDay) = rules.MonthDay("W5_TRANSFER_DEADLINE", start);

            usageStart = start;
            usageEnd = end.AddMonths(rules.Int("W15_USAGE_END_MONTHS_AFTER_EARNING", start));
            transferDeadline = new DateOnly(end.Year, transferMonth, transferDay);
        }

        return new HolidayBucket
        {
            EmployeeId = employeeId,
            AccountTypeId = account.AccountTypeId,
            EarningStart = ToDateTime(start),
            EarningEnd = ToDateTime(end),
            UsageStart = ToDateTime(usageStart),
            UsageEnd = ToDateTime(usageEnd),
            TransferDeadline = ToDateTime(transferDeadline),
            Status = "OPEN",
        };
    }

    /// <summary>The first day of the earning period that the date falls in, e.g. 1 Sep for weeks 1-5.</summary>
    private static DateOnly PeriodStartContaining(DateOnly date, int month, int day)
    {
        var thisYear = new DateOnly(date.Year, month, day);
        return date >= thisYear ? thisYear : thisYear.AddYears(-1);
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static DateOnly ToDate(DateTime value) => DateOnly.FromDateTime(value);

    private static DateTime ToDateTime(DateOnly value) => value.ToDateTime(TimeOnly.MinValue);

    /// <summary>One employee's schedule history: which schedule was in force on a date, and what it means in hours.</summary>
    private sealed class ScheduleBook(List<WorkSchedule> schedules, List<WorkScheduleDay> days)
    {
        /// <summary>Weekly hours, and the average length of a working day (weekly hours / working days).</summary>
        public (decimal WeeklyHours, decimal DayLength) On(DateOnly date)
        {
            var schedule = schedules.FirstOrDefault(s => Covers(s, date))
                           ?? schedules.LastOrDefault(s => ToDate(s.ValidFrom) <= date)
                           ?? schedules[0];

            var rows = days.Where(d => d.ScheduleId == schedule.ScheduleId).ToList();
            var weekly = rows.Sum(d => d.Hours);
            var working = rows.Count(d => d.Hours > 0);

            return (weekly, working == 0 ? 0 : weekly / working);
        }

        private static bool Covers(WorkSchedule s, DateOnly date) =>
            ToDate(s.ValidFrom) <= date && (s.ValidTo is null || ToDate(s.ValidTo.Value) >= date);
    }
}