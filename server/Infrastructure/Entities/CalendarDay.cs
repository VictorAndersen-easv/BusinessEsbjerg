using LinqToDB;
using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>A public holiday or a company day off. These never count as holiday days.</summary>
[Table("calendar_day")]
public class CalendarDay
{
    [PrimaryKey, Column("date", DataType = DataType.Date)]
    public DateTime Date { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    /// <summary>PUBLIC_HOLIDAY or COMPANY_DAY_OFF.</summary>
    [Column("day_type")]
    public string DayType { get; set; } = "";
}   