using System.Globalization;
using Infrastructure;
using Infrastructure.Entities;
using LinqToDB;
using LinqToDB.Async;

namespace Service;

/// <summary>
/// When a working schedule changes, holiday is still a right to DAYS off. So the number of days in a
/// balance stays the same, and the hours are recalculated with the length of a day in the new schedule.
/// The difference is booked as a CONVERSION row (days = 0, hours = the change). Payroll handles any
/// money difference (feriedifference) from the schedule history, so nothing about money is stored here.
/// </summary>
public class ScheduleConversionService(ExamDatabase db)
{
    /// <summary>
    /// Converts the balances for every schedule of the employee that has started (ValidFrom up to asOf)
    /// and has not been converted yet. Safe to call again: a converted schedule leaves a marker row behind.
    /// </summary>
    /// <returns>How many CONVERSION rows were booked.</returns>
    public async Task<int> ConvertDueAsync(int employeeId, DateOnly asOf, int? actingAccountId)
    {
        var schedules = await db.WorkSchedules
            .Where(s => s.EmployeeId == employeeId)
            .OrderBy(s => s.ValidFrom)
            .ToListAsync();

        if (schedules.Count < 2) return 0; // the first schedule has nothing to convert from

        var scheduleIds = schedules.Select(s => s.ScheduleId).ToList();
        var days = await db.WorkScheduleDays.Where(d => scheduleIds.Contains(d.ScheduleId)).ToListAsync();

        var buckets = await db.HolidayBuckets
            .Where(b => b.EmployeeId == employeeId && b.Status == "OPEN")
            .ToListAsync();
        if (buckets.Count == 0) return 0;

        var bucketIds = buckets.Select(b => b.BucketId).ToList();
        var entries = await db.LedgerEntries.Where(l => bucketIds.Contains(l.BucketId)).ToListAsync();

        var created = 0;

        for (var i = 1; i < schedules.Count; i++)
        {
            var schedule = schedules[i];
            var startsOn = ToDate(schedule.ValidFrom);
            if (startsOn > asOf) break; // not started yet (and neither are the later ones)

            var oldLength = DayLength(days, schedules[i - 1].ScheduleId);
            var newLength = DayLength(days, schedule.ScheduleId);

            foreach (var bucket in buckets)
            {
                var rows = entries.Where(e => e.BucketId == bucket.BucketId).ToList();

                // Already converted for this schedule (a row, even with 0 hours, is the marker)
                if (rows.Any(r => r.EntryType == "CONVERSION" && r.ScheduleId == schedule.ScheduleId)) continue;

                // Nothing was earned or used under the old schedule, so there is nothing to convert
                if (!rows.Any(r => ToDate(r.EntryDate) < startsOn)) continue;

                var remainingDays = rows.Sum(r => r.Days);
                var remainingHours = rows.Sum(r => r.Hours);

                // The days stay, the hours become days x the new day length
                var newHours = Math.Round(remainingDays * newLength, 2, MidpointRounding.AwayFromZero);

                var entry = new LedgerEntry
                {
                    BucketId = bucket.BucketId,
                    EntryType = "CONVERSION",
                    EntryDate = startsOn.ToDateTime(TimeOnly.MinValue),
                    Hours = newHours - remainingHours,
                    Days = 0,
                    ScheduleId = schedule.ScheduleId,
                    Note = $"Schedule change from {startsOn:yyyy-MM-dd}: day length "
                           + $"{oldLength.ToString("0.00", CultureInfo.InvariantCulture)} h to "
                           + $"{newLength.ToString("0.00", CultureInfo.InvariantCulture)} h",
                    CreatedBy = actingAccountId,
                };

                await db.InsertAsync(entry);
                entries.Add(entry);
                created++;
            }
        }

        return created;
    }

    /// <summary>The average length of a working day in a schedule: weekly hours / working days.</summary>
    private static decimal DayLength(List<WorkScheduleDay> days, int scheduleId)
    {
        var rows = days.Where(d => d.ScheduleId == scheduleId).ToList();
        var working = rows.Count(d => d.Hours > 0);
        return working == 0 ? 0 : rows.Sum(d => d.Hours) / working;
    }

    private static DateOnly ToDate(DateTime value) => DateOnly.FromDateTime(value);
}