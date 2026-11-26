using LinqToDB;
using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>
/// One calendar day of an approved request: what it cost and which bucket paid for it.
/// Weekends, public holidays and days off are listed too, with 0 hours.
/// </summary>
[Table("holiday_request_day")]
public class HolidayRequestDay
{
    [PrimaryKey(1), Column("request_id")]
    public int RequestId { get; set; }

    [PrimaryKey(2), Column("date", DataType = DataType.Date)]
    public DateTime Date { get; set; }

    [Column("hours_deducted")]
    public decimal HoursDeducted { get; set; }

    [Column("days_deducted")]
    public decimal DaysDeducted { get; set; }

    [Column("bucket_id")]
    public int? BucketId { get; set; }
}