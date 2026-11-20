using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>One row in `user_account`: a login. See <see cref="Roles"/> for the three kinds.</summary>
[Table("user_account")]
public class UserAccount
{
    [PrimaryKey, Identity, Column("account_id")]
    public int AccountId { get; set; }

    [Column("email")]
    public string Email { get; set; } = "";

    /// <summary>BCrypt hash. The real password is never stored.</summary>
    [Column("password_hash")]
    public string PasswordHash { get; set; } = "";

    [Column("role")]
    public string Role { get; set; } = Roles.Employee;

    /// <summary>Set for EMPLOYEE logins, null for the accountant and the system owner.</summary>
    [Column("employee_id")]
    public int? EmployeeId { get; set; }

    /// <summary>Only used by the accountant and system owner logins.</summary>
    [Column("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>The database fills this in itself, so we never send it.</summary>
    [Column("created_at", SkipOnInsert = true)]
    public DateTime CreatedAt { get; set; }
}