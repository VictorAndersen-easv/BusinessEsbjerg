using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace api.Auth;

/// <summary>Creates signed JWT tokens ("wristbands") for logged-in users.</summary>
public class TokenService(IConfiguration config)
{
    public string CreateToken(UserDto user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));

        var claims = new List<Claim>
        {
            new("sub", user.AccountId.ToString()),   // the LOGIN, not the employee
            new("name", user.Name),
            new("email", user.Email),
            new("role", user.Role),
        };

        // Only employee logins have an employee id. Endpoints about "my holiday" read it from here.
        if (user.EmployeeId is int employeeId)
            claims.Add(new Claim("employee_id", employeeId.ToString()));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = config["Jwt:Issuer"],
            Audience = config["Jwt:Audience"],
            Expires = DateTime.UtcNow.AddHours(8),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}