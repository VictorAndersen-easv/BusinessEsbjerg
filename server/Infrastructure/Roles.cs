namespace Infrastructure;

/// <summary>The three kinds of login (user_account.role).</summary>
public static class Roles
{
    /// <summary>A normal employee: requests holiday, sees the calendar and their own balance.</summary>
    public const string Employee = "EMPLOYEE";

    /// <summary>The one accountant login: approves or declines requests, can edit schedules.</summary>
    public const string Accountant = "ACCOUNTANT";

    /// <summary>The one system owner login: creates users, edits schedules, global rules and calendar days.</summary>
    public const string SystemOwner = "SYSTEM_OWNER";
}