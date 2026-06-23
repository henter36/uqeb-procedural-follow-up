using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Uqeb.Api.Tests;

internal static class TestJwtHelper
{
    private const string Key = "integration-test-jwt-key-32-chars-min";
    private const string Issuer = "UqebApiTests";
    private const string Audience = "UqebClientTests";

    public static string CreateToken(string role)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim(ClaimTypes.Name, "integration-test-user"),
                new Claim(ClaimTypes.Role, role),
            ]),
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
