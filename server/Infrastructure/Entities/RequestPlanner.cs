using System.Globalization;
using Infrastructure;
using Infrastructure.Entities;
using LinqToDB;
using LinqToDB.Async;

namespace Service;

/// <summary>Submit: someone is making a request (the notice rules apply). Approve: the accountant is deciding.</summary>
public enum PlanMode { Submit, Approve }

/// <summary>One holiday day paid from one bucket. On approval these become USAGE rows in the ledger.</summary>
public record PlannedUsage(DateOnly Date, int BucketId, decimal Hours, decimal Days);

public record PlanResult(RequestPreviewDto Preview, List<PlannedUsage> Usages);

/// <summary>
/// Works out what a holiday request costs, day by day, and checks every rule. It saves nothing.
/// The same code answers the live preview, the submit and the approval, so they can never disagree.
/// </summary>
public class RequestPlanner(ExamDatabase db)
{
    private const string Weeks15 = "WEEKS_1_5";
    private const string Week6 = "WEEK_6";

    public async Task<PlanResult> PlanAsync(int employeeId, RequestInput input, PlanMode mode, int? excludeRequestId = null)
    {
        var start = input.StartDate;
        var end = input.EndDate;

        if (end < start)
            throw new RuleViolationException("The end date cannot be before the start date.");
        if (end.DayNumber - start.DayNumber > 366)
            throw new RuleViolationException("A request can cover at most one year.");

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId)
                       ?? throw new NotFoundException("Employee not found.");
        var account = await db.HolidayAccountTypes.FirstOrDefaultAsync(a => a.Code == input.Account)
                      ?? throw new RuleViolationException("Unknown holiday account. Use WEEKS_1_5 or WEEK_6.");
        var rules = await RuleBook.LoadAsync(db);

        var scheduleRows = await db.WorkSchedules.Where(s => s.EmployeeId == employeeId).OrderBy(s => s.ValidFrom).ToListAsync();
        if (scheduleRows.Count == 0)
            throw new RuleViolationException("No working schedule has been set up yet. Ask the accountant.");
        var scheduleIds = scheduleRows.Select(s => s.ScheduleId).ToList();
        var scheduleDays = await db.WorkScheduleDays.Where(d => scheduleIds.Contains(d.ScheduleId)).ToListAsync();
        var schedules = new ScheduleBook(scheduleRows, scheduleDays);

        var startDt = ToDateTime(start);
        var endDt = ToDateTime(end);
        var calendar = (await db.CalendarDays.Where(c => c.Date >= startDt && c.Date <= endDt).ToListAsync())
            .ToDictionary(c => ToDate(c.Date));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var problems = new List<string>();
        var warnings = new List<string>();

        // ---------------------------------------------------------------- the dates
        if (mode == PlanMode.Submit && start <= today)
            problems.Add("Holiday must start in the future.");
        if (mode == PlanMode.Approve && start < today)
            problems.Add("The holiday has already started. Decline the request instead.");
        if (start < ToDate(employee.HireDate))
            problems.Add("Holiday cannot start before the hire date.");
        if (employee.TerminationDate is { } lastDayAtWork && end > ToDate(lastDayAtWork))
            problems.Add("Holiday cannot end after the last day of employment.");

        // ---------------------------------------------------------------- day by day
        var lines = new List<PlannedDayDto>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                lines.Add(new PlannedDayDto(day, "WEEKEND", 0, 0, null));
            }
            else if (calendar.TryGetValue(day, out var offDay))
            {
                lines.Add(new PlannedDayDto(day, offDay.DayType, 0, 0, offDay.Name));
            }
            else
            {
                var hours = schedules.HoursOn(day);
                if (hours <= 0)
                {
                    lines.Add(new PlannedDayDto(day, "NOT_WORKING", 0, 0, "Not a working day in the schedule"));
                }
                else
                {
                    // Weeks 1-5 are counted in days (1 per day). Week 6 is kept in hours, so its days are hours / day length.
                    var (_, dayLength) = schedules.On(day);
                    var days = account.Code == Week6 ? Round2(hours / dayLength) : 1m;
                    lines.Add(new PlannedDayDto(day, "HOLIDAY", hours, days, null));
                }
            }
        }

        var holidayLines = lines.Where(l => l.Kind == "HOLIDAY").ToList();
        var totalHours = holidayLines.Sum(l => l.Hours);
        var totalDays = holidayLines.Sum(l => l.Days);

        if (holidayLines.Count == 0)
            problems.Add("These dates contain no holiday days (weekends, public holidays and days off do not count).");

        // ---------------------------------------------------------------- the account and the notice
        if (account.Code == Week6 && !rules.Bool("W6_ENABLED", today))
            problems.Add("The 6th holiday week is not in use.");

        if (mode == PlanMode.Submit && account.Code == Weeks15)
        {
            var months = input.IsMainHoliday ? rules.Int("NOTICE_MAIN_MONTHS", today) : rules.Int("NOTICE_OTHER_MONTHS", today);
            var earliest = today.AddMonths(months);
            if (months > 0 && start < earliest)
                problems.Add($"{(input.IsMainHoliday ? "A main holiday" : "Holiday")} must be requested at least {months} month(s) ahead. The earliest start date is {Fmt(earliest)}.");
        }

        // ---------------------------------------------------------------- the main holiday
        var live = new[] { "SUBMITTED", "APPROVED", "CANCELLATION_REQUESTED" };
        var exclude = excludeRequestId ?? 0;

        if (input.IsMainHoliday)
        {
            if (account.Code != Weeks15)
                problems.Add("The main holiday must be taken from weeks 1-5.");

            var (windowStartMonth, windowStartDay) = rules.MonthDay("MAIN_HOLIDAY_WINDOW_START", today);
            var (windowEndMonth, windowEndDay) = rules.MonthDay("MAIN_HOLIDAY_WINDOW_END", today);
            var windowStart = new DateOnly(start.Year, windowStartMonth, windowStartDay);
            var windowEnd = new DateOnly(start.Year, windowEndMonth, windowEndDay);

            if (start < windowStart || end > windowEnd)
                problems.Add($"A main holiday must lie between {Fmt(windowStart)} and {Fmt(windowEnd)}.");

            // 15 days is 3 weeks for someone who works 5 days a week. A part-timer is entitled to the same
            // 3 weeks, so the number of days is scaled by how many days a week the person works.
            var baseDays = rules.Int("MAIN_HOLIDAY_DAYS", today);
            var minimumDays = (int)Math.Ceiling(baseDays * schedules.WorkingDaysOn(start) / 5m);
            if (holidayLines.Count < minimumDays)
                problems.Add($"A main holiday must be 3 weeks of your working days, which is {minimumDays} holiday days. These dates have {holidayLines.Count}.");

            var windowStartDt = ToDateTime(windowStart);
            var windowEndDt = ToDateTime(windowEnd);
            var anotherMain = await db.HolidayRequests.AnyAsync(r =>
                r.EmployeeId == employeeId && r.IsMainHoliday && live.Contains(r.Status)
                && r.RequestId != exclude && r.StartDate >= windowStartDt && r.StartDate <= windowEndDt);
            if (anotherMain)
                problems.Add("There is already a main holiday request for that summer.");
        }
        else if (account.Code == Weeks15 && holidayLines.Count is > 0 and < 5)
        {
            warnings.Add("Where possible, holiday other than the main holiday should be taken in blocks of at least 5 days.");
        }

        // ---------------------------------------------------------------- overlap with the person's other requests
        var overlapping = await db.HolidayRequests
            .Where(r => r.EmployeeId == employeeId && live.Contains(r.Status) && r.RequestId != exclude
                        && r.StartDate <= endDt && r.EndDate >= startDt)
            .ToListAsync();
        if (overlapping.Count > 0)
            problems.Add($"There is already a request on these dates ({Fmt(ToDate(overlapping[0].StartDate))} to {Fmt(ToDate(overlapping[0].EndDate))}).");

        // ---------------------------------------------------------------- which bucket pays, and is the balance enough?
        var usages = new List<PlannedUsage>();

        if (holidayLines.Count > 0)
        {
            var buckets = await db.HolidayBuckets
                .Where(b => b.EmployeeId == employeeId && b.AccountTypeId == account.AccountTypeId && b.Status == "OPEN")
                .ToListAsync();
            var bucketIds = buckets.Select(b => b.BucketId).ToList();
            var ledger = await db.LedgerEntries.Where(l => bucketIds.Contains(l.BucketId)).ToListAsync();

            // Look far enough ahead to include the last booked holiday and, for weeks 1-5, the next 31 August
            var latestUsage = ledger.Where(l => l.EntryType == "USAGE").Select(l => ToDate(l.EntryDate)).DefaultIfEmpty(end).Max();
            var horizon = latestUsage > end ? latestUsage : end;
            var (checkMonth, checkDay) = rules.MonthDay("DEBT_CHECK_DATE", today);
            if (account.Code == Weeks15)
            {
                var nextCheck = new DateOnly(horizon.Year, checkMonth, checkDay);
                horizon = nextCheck >= horizon ? nextCheck : nextCheck.AddYears(1);
            }

            // The ledger plus the holiday that WILL be earned by then (not saved anywhere)
            var rows = ledger.ToList();
            foreach (var bucket in buckets)
            {
                var mine = ledger.Where(l => l.BucketId == bucket.BucketId);
                rows.AddRange(AccrualService.ProjectAccruals(employee, account, bucket, mine, schedules, rules, horizon.AddDays(1)));
            }

            // Weeks 1-5 are measured in days, week 6 in hours
            decimal Unit(LedgerEntry row) => account.Code == Week6 ? row.Hours : row.Days;
            bool InWindow(HolidayBucket b, DateOnly date) => ToDate(b.UsageStart) <= date && date <= ToDate(b.UsageEnd);

            decimal BalanceAt(DateOnly date, int? onlyBucket = null)
            {
                var ids = buckets
                    .Where(b => InWindow(b, date) && (onlyBucket is null || b.BucketId == onlyBucket))
                    .Select(b => b.BucketId)
                    .ToHashSet();
                return rows.Where(r => ids.Contains(r.BucketId) && ToDate(r.EntryDate) <= date).Sum(Unit);
            }

            // Each day is paid from the bucket that expires first. If none can afford it, the newest bucket goes into debt.
            var missingBucket = false;
            foreach (var line in holidayLines)
            {
                var usable = buckets
                    .Where(b => InWindow(b, line.Date))
                    .OrderBy(b => b.UsageEnd)
                    .ThenBy(b => b.EarningStart)
                    .ToList();

                if (usable.Count == 0)
                {
                    if (!missingBucket)
                        problems.Add($"No holiday can be used on {Fmt(line.Date)}: it has not been earned yet, or it has expired.");
                    missingBucket = true;
                    continue;
                }

                var cost = account.Code == Week6 ? line.Hours : 1m;
                var chosen = usable.FirstOrDefault(b => BalanceAt(line.Date, b.BucketId) >= cost) ?? usable[^1];

                rows.Add(new LedgerEntry
                {
                    BucketId = chosen.BucketId,
                    EntryType = "USAGE",
                    EntryDate = ToDateTime(line.Date),
                    Hours = -line.Hours,
                    Days = -line.Days,
                });
                usages.Add(new PlannedUsage(line.Date, chosen.BucketId, line.Hours, line.Days));
            }

            // The debt rules. Weeks 1-5 may go up to DEBT_MAX_DAYS into debt but must be 0 or more on 31 August.
            // Week 6 may not go into debt at all. We check every day on which holiday is taken, from this
            // request on, so the new request cannot break a holiday that was approved earlier.
            if (!missingBucket)
            {
                var allowance = account.Code == Weeks15 ? rules.Decimal("DEBT_MAX_DAYS", today) : 0m;

                var checkDates = new HashSet<DateOnly>(
                    rows.Where(r => r.EntryType == "USAGE").Select(r => ToDate(r.EntryDate)).Where(d => d >= start));

                var augustDates = new HashSet<DateOnly>();
                if (account.Code == Weeks15)
                {
                    for (var year = start.Year; year <= horizon.Year; year++)
                    {
                        var date = new DateOnly(year, checkMonth, checkDay);
                        if (date >= start && date <= horizon) augustDates.Add(date);
                    }
                    checkDates.UnionWith(augustDates);
                }

                var debtReported = false;
                var augustReported = false;

                foreach (var date in checkDates.OrderBy(d => d))
                {
                    var balance = BalanceAt(date);

                    if (!debtReported && balance < -allowance)
                    {
                        problems.Add(account.Code == Week6
                            ? $"Not enough week-6 holiday: you would be {Number(-balance)} hours short on {Fmt(date)}."
                            : $"Not enough holiday: the balance would be {Number(balance)} days on {Fmt(date)}, and at most {Number(allowance)} days of debt are allowed.");
                        debtReported = true;
                    }

                    if (!augustReported && augustDates.Contains(date) && balance < 0)
                    {
                        problems.Add($"The holiday balance would be negative on {Fmt(date)}. It must be zero or more on that day.");
                        augustReported = true;
                    }
                }
            }
        }

        var preview = new RequestPreviewDto(
            account.Code, start, end, totalHours, totalDays,
            lines.ToArray(), problems.ToArray(), warnings.ToArray(), problems.Count == 0);

        return new PlanResult(preview, usages);
    }

    private static string Fmt(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static DateOnly ToDate(DateTime value) => DateOnly.FromDateTime(value);

    private static DateTime ToDateTime(DateOnly value) => value.ToDateTime(TimeOnly.MinValue);
}