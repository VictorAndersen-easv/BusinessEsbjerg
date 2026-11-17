using LinqToDB;
using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>One row in the `employee` table. Each property = one column.</summary>
[Table("employee")]
public class Employee
{
    [PrimaryKey, Identity, Column("employee_id")]
    public int EmployeeId { get; set; }

    [Column("employee_no")]
    public string EmployeeNo { get; set; } = "";

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("email")]
    public string Email { get; set; } = "";

    /// <summary>BCrypt hash. The real password is never stored.</summary>
    [Column("password_hash")]
    public string? PasswordHash { get; set; }

    /// <summary>EMPLOYEE, ACCOUNTING or ADMIN (see <see cref="Roles"/>).</summary>
    [Column("role")]
    public string Role { get; set; } = Roles.Employee;

    [Column("manager_id")]
    public int? ManagerId { get; set; }

    [Column("hire_date", DataType = DataType.Date)]
    public DateTime HireDate { get; set; }

    [Column("termination_date", DataType = DataType.Date)]
    public DateTime? TerminationDate { get; set; }

    [Column("termination_reason")]
    public string? TerminationReason { get; set; }
}