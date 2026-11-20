namespace api.Auth;

/// <summary>Names of the authorization policies. They are defined in Program.cs.</summary>
public static class Policies
{
    /// <summary>The accountant or the system owner.</summary>
    public const string Staff = "Staff";

    /// <summary>Only the system owner.</summary>
    public const string SystemOwner = "SystemOwner";
}