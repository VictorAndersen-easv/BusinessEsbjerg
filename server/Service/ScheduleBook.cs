using Infrastructure.Entities;

namespace Service;

/// <summary>
/// One employee's schedule history. Answers "which schedule was in force on this date?"
/// and what that means in hours.
/// </summary>
public sealed class ScheduleBook
{
    private readonly List<WorkSchedule> _schedules; // oldest first
    private readonly List<WorkScheduleDay> _days;

    public ScheduleBook(List<WorkSchedule> schedules, List<WorkScheduleDay> days)
    {
        _schedules = schedules;
        _days = days;
    }

    /// <summary>Weekly hours, and the average length of a working day (weekly hours / working days).</summary>
    public (decimal WeeklyHours, decimal DayLength) On(DateOnly date)
    {
        var schedule = ScheduleFor(date);
        var rows = _days.Where(d => d.ScheduleId == schedule.ScheduleId).ToList();
        var weekly = rows.Sum(d => d.Hours);
        var working = rows.Count(d => d.Hours > 0);

        return (weekly, working == 0 ? 0 : weekly / working);
    }

    /// <summary>How many weekdays a week this person works in the schedule that applies on that date.</summary>
    public int WorkingDaysOn(DateOnly date)
    {
        var schedule = ScheduleFor(date);
        return _days.Count(d => d.ScheduleId == schedule.ScheduleId && d.Hours > 0);
    }

    /// <summary>The hours this person works on that particular date. 0 on weekends and on their days off.</summary>
    public decimal HoursOn(DateOnly date)
    {
        var weekday = ((int)date.DayOfWeek + 6) % 7 + 1; // Monday = 1 ... Sunday = 7
        if (weekday > 5) return 0;

        var schedule = ScheduleFor(date);
        return _days.FirstOrDefault(d => d.ScheduleId == schedule.ScheduleId && d.Weekday == weekday)?.Hours ?? 0;
    }

    private WorkSchedule ScheduleFor(DateOnly date) =>
        _schedules.FirstOrDefault(s => Covers(s, date))
        ?? _schedules.LastOrDefault(s => ToDate(s.ValidFrom) <= date)
        ?? _schedules[0];

    private static bool Covers(WorkSchedule s, DateOnly date) =>
        ToDate(s.ValidFrom) <= date && (s.ValidTo is null || ToDate(s.ValidTo.Value) >= date);

    private static DateOnly ToDate(DateTime value) => DateOnly.FromDateTime(value);
}