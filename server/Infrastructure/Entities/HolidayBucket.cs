using LinqToDB;
using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>One account (weeks 1-5 or week 6) for one earning period of one employee.</summary>
[Table("holiday_bucket")]
public class HolidayBucket
{
    [PrimaryKey, Identity, Column("bucket_id")]
    public int BucketId { get; set; }

    [Column("employee_id")]
    public int EmployeeId { get; set; }

    [Column("account_type_id")]
    public int AccountTypeId { get; set; }

    [Column("earning_start", DataType = DataType.Date)]
    public DateTime EarningStart { get; set; }

    [Column("earning_end", DataType = DataType.Date)]
    public DateTime EarningEnd { get; set; }

    /// <summary>First day the earned holiday may be used.</summary>
    [Column("usage_start", DataType = DataType.Date)]
    public DateTime UsageStart { get; set; }

    /// <summary>Last day the earned holiday may be used. After this it expires.</summary>
    [Column("usage_end", DataType = DataType.Date)]
    public DateTime UsageEnd { get; set; }

    [Column("transfer_deadline", DataType = DataType.Date)]
    public DateTime? TransferDeadline { get; set; }

    /// <summary>OPEN, EXPIRED or SETTLED.</summary>
    [Column("status")]
    public string Status { get; set; } = "OPEN";
}