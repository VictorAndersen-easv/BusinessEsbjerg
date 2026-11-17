using System.Security.Claims;
using System.Text;
using Infrastructure.Entities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace api.Auth;

/// <summary>Creates signed JWT tokens ("wristbands") for logged-in employees.</summary>
public class TokenService(IConfiguration config)
{
    public string CreateToken(Employee employee)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", employee.EmployeeId.ToString()),
                new Claim("name", employee.Name),
                new Claim("email", employee.Email),
                new Claim("role", employee.Role),
            }),
            Issuer = config["Jwt:Issuer"],
            Audience = config["Jwt:Audience"],
            Expires = DateTime.UtcNow.AddHours(8),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}