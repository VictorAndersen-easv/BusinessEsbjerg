using LinqToDB;
using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>
/// One weekly schedule that was in use for a period of whole weeks (Monday to Sunday).
/// A schedule is never edited: a change closes this one and adds a new one.
/// </summary>
[Table("work_schedule")]
public class WorkSchedule
{
    [PrimaryKey, Identity, Column("schedule_id")]
    public int ScheduleId { get; set; }

    [Column("employee_id")]
    public int EmployeeId { get; set; }

    /// <summary>Always a Monday.</summary>
    [Column("valid_from", DataType = DataType.Date)]
    public DateTime ValidFrom { get; set; }

    /// <summary>Always a Sunday. Null means "still in use".</summary>
    [Column("valid_to", DataType = DataType.Date)]
    public DateTime? ValidTo { get; set; }

    [Column("created_by")]
    public int? CreatedBy { get; set; }

    [Column("created_at", SkipOnInsert = true)]
    public DateTime CreatedAt { get; set; }
}