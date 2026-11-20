using LinqToDB;
using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>One weekday (1 = Monday ... 5 = Friday) of a schedule. A day off has no times and 0 hours.</summary>
[Table("work_schedule_day")]
public class WorkScheduleDay
{
    [PrimaryKey(1), Column("schedule_id")]
    public int ScheduleId { get; set; }

    [PrimaryKey(2), Column("weekday")]
    public short Weekday { get; set; }

    [Column("start_time", DataType = DataType.Time)]
    public TimeSpan? StartTime { get; set; }

    [Column("end_time", DataType = DataType.Time)]
    public TimeSpan? EndTime { get; set; }

    /// <summary>End minus start, two decimals. The database checks that this matches the times.</summary>
    [Column("hours")]
    public decimal Hours { get; set; }
}