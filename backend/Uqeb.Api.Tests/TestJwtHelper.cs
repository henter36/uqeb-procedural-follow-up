using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Uqeb.Api.Authorization;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Tests;

internal static class TestJwtHelper
{
    private const string Key = "integration-test-jwt-key-32-chars-min";
    private const string Issuer = "UqebApiTests";
    private const string Audience = "UqebClientTests";

    public static string CreateToken(string role, int? departmentId = null, int userId = 1)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, "integration-test-user"),
            new(ClaimTypes.Role, role),
        };

        if (Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsedRole))
        {
            claims.AddRange(RolePermissionDefaults
                .GetPermissions(parsedRole)
                .Select(permission => new Claim(PermissionClaims.PermissionClaimType, permission.ToString())));
        }

        if (departmentId.HasValue)
            claims.Add(new Claim("departmentId", departmentId.Value.ToString()));

        var tokenHandler = new JwtSecurityTokenHandler();
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(
                signingKey,
                SecurityAlgorithms.HmacSha256Signature),
        };

        return tokenHandler.WriteToken(tokenHandler.CreateToken(descriptor));
    }
}
