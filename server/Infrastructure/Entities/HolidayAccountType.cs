using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>One row in `holiday_account_type`: WEEKS_1_5 or WEEK_6.</summary>
[Table("holiday_account_type")]
public class HolidayAccountType
{
    [PrimaryKey, Identity, Column("account_type_id")]
    public int AccountTypeId { get; set; }

    [Column("code")]
    public string Code { get; set; } = "";

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("native_unit")]
    public string NativeUnit { get; set; } = "";
}