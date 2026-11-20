using LinqToDB;
using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>
/// One row in the `employee` table: a person who earns holiday.
/// Logins (email, password, role) live in <see cref="UserAccount"/>.
/// </summary>
[Table("employee")]
public class Employee
{
    [PrimaryKey, Identity, Column("employee_id")]
    public int EmployeeId { get; set; }

    [Column("employee_no")]
    public string EmployeeNo { get; set; } = "";

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("hire_date", DataType = DataType.Date)]
    public DateTime HireDate { get; set; }

    [Column("termination_date", DataType = DataType.Date)]
    public DateTime? TerminationDate { get; set; }

    [Column("termination_reason")]
    public string? TerminationReason { get; set; }
}