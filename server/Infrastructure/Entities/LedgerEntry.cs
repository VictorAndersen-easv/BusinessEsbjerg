using LinqToDB;
using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>
/// One signed row in the holiday ledger. A balance is the SUM of rows, never a stored number.
/// Rows are never changed or deleted (the database refuses): mistakes are fixed with a REVERSAL row.
/// </summary>
[Table("ledger_entry")]
public class LedgerEntry
{
    [PrimaryKey, Identity, Column("entry_id")]
    public long EntryId { get; set; }

    [Column("bucket_id")]
    public int BucketId { get; set; }

    /// <summary>ACCRUAL, USAGE, TRANSFER_OUT, TRANSFER_IN, PAYOUT, ADJUSTMENT, REVERSAL or CONVERSION.</summary>
    [Column("entry_type")]
    public string EntryType { get; set; } = "";

    [Column("entry_date", DataType = DataType.Date)]
    public DateTime EntryDate { get; set; }

    [Column("hours")]
    public decimal Hours { get; set; }

    [Column("days")]
    public decimal Days { get; set; }

    [Column("request_id")]
    public int? RequestId { get; set; }

    [Column("payout_id")]
    public int? PayoutId { get; set; }

    [Column("transfer_id")]
    public int? TransferId { get; set; }

    [Column("schedule_id")]
    public int? ScheduleId { get; set; }

    [Column("note")]
    public string? Note { get; set; }

    /// <summary>The login that caused the entry. Null means a system job (like the monthly accrual).</summary>
    [Column("created_by")]
    public int? CreatedBy { get; set; }

    [Column("created_at", SkipOnInsert = true)]
    public DateTime CreatedAt { get; set; }
}