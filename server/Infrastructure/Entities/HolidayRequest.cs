using LinqToDB;
using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>One row in `holiday_request`.</summary>
[Table("holiday_request")]
public class HolidayRequest
{
    [PrimaryKey, Identity, Column("request_id")]
    public int RequestId { get; set; }

    [Column("employee_id")]
    public int EmployeeId { get; set; }

    [Column("account_type_id")]
    public int AccountTypeId { get; set; }

    [Column("start_date", DataType = DataType.Date)]
    public DateTime StartDate { get; set; }

    [Column("end_date", DataType = DataType.Date)]
    public DateTime EndDate { get; set; }

    [Column("is_main_holiday")]
    public bool IsMainHoliday { get; set; }

    /// <summary>
    /// SUBMITTED, APPROVED, REJECTED, CANCELLED_BY_EMPLOYEE (withdrawn before a decision),
    /// CANCELLATION_REQUESTED, CANCELLED or CANCELLED_BY_EMPLOYER.
    /// </summary>
    [Column("status")]
    public string Status { get; set; } = "SUBMITTED";

    /// <summary>Snapshot of the hours this request costs.</summary>
    [Column("total_hours")]
    public decimal TotalHours { get; set; }

    /// <summary>Snapshot of the days this request costs.</summary>
    [Column("total_days")]
    public decimal TotalDays { get; set; }

    /// <summary>The database fills this in itself (DEFAULT now()), so we never send it.</summary>
    [Column("submitted_at", SkipOnInsert = true)]
    public DateTime SubmittedAt { get; set; }

    /// <summary>The user_account (accountant login) that decided.</summary>
    [Column("decided_by")]
    public int? DecidedBy { get; set; }

    [Column("decided_at")]
    public DateTime? DecidedAt { get; set; }

    [Column("decision_note")]
    public string? DecisionNote { get; set; }

    [Column("cancellation_requested_at")]
    public DateTime? CancellationRequestedAt { get; set; }

    [Column("cancel_reason")]
    public string? CancelReason { get; set; }

    [Column("cancel_decided_by")]
    public int? CancelDecidedBy { get; set; }

    [Column("cancel_decided_at")]
    public DateTime? CancelDecidedAt { get; set; }

    [Column("cancel_decision_note")]
    public string? CancelDecisionNote { get; set; }
}