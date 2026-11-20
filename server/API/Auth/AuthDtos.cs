namespace api.Auth;

// DTOs ("data transfer objects") = the exact shapes sent over HTTP.
// They match the types in the frontend's Types.ts.
public record LoginRequest(string Email, string Password);

/// <summary>EmployeeId is null for the accountant and system owner logins.</summary>
public record UserDto(int AccountId, int? EmployeeId, string Name, string Email, string Role);

public record LoginResponse(string Token, UserDto User);