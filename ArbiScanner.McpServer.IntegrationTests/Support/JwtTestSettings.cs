using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace ArbiScanner.McpServer.IntegrationTests.Support;

// Fast, no-Keycloak auth path: forge a JWT signed with a known test key instead of
// validating against a real Authority/JWKS endpoint. Standard Token Exchange against a
// real Keycloak is already covered end-to-end by ArbiScannerWeb.IntegrationTests'
// McpTokenControllerTests — what this project needs to prove is that its OWN hosted
// pipeline (DI, MapMcp wiring, tool execution, downstream forwarding) works, which
// doesn't require re-validating Keycloak's JWKS machinery a second time.
internal static class JwtTestSettings
{
    public const string SigningKey = "integration-tests-signing-key-32-chars-minimum";
    public const string Issuer = "ArbiScanner.McpServer.IntegrationTests";
    public const string Audience = "arbiscanner-mcp";

    public static readonly Dictionary<string, string?> ConfigOverrides = new()
    {
        ["Keycloak:Authority"] = string.Empty,
        ["Keycloak:Audience"] = Audience,
    };

    // Overrides the JwtBearer options AddAuthenticationJwt configured from
    // Keycloak:Authority with a static symmetric key, mirroring what a real
    // Keycloak-issued token validates against but without any network dependency.
    public static void ConfigureTestJwtBearer(IServiceCollection services)
    {
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(SigningKey)),
                ValidateIssuerSigningKey = true,
                NameClaimType = "sub",
            };
        });
    }
}
