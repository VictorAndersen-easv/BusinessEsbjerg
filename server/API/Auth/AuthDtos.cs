namespace api.Auth;

// DTOs ("data transfer objects") = the exact shapes sent over HTTP.
// They match the types in the frontend's types.ts.
public record LoginRequest(string Email, string Password);

public record UserDto(int EmployeeId, string Name, string Email, string Role);

public record LoginResponse(string Token, UserDto User);