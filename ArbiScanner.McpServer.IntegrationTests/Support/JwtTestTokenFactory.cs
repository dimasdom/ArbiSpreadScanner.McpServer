using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ArbiScanner.McpServer.IntegrationTests.Support;

// Forges a JWT shaped like a real Keycloak-issued access token (sub claim, arbiscanner-mcp
// audience) signed with JwtTestSettings' known test key.
internal static class JwtTestTokenFactory
{
    public static string CreateToken(string sub, TimeSpan? lifetime = null)
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime ?? TimeSpan.FromMinutes(15));
        // A negative lifetime (an already-expired test token) would otherwise put
        // Expires before the default NotBefore=now, which JwtSecurityToken rejects.
        var notBefore = expires < now ? expires.AddMinutes(-1) : now;

        var jwt = new JwtSecurityToken(
            issuer: JwtTestSettings.Issuer,
            audience: JwtTestSettings.Audience,
            claims: [new Claim("sub", sub)],
            notBefore: notBefore,
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(JwtTestSettings.SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
