namespace ArbiScanner.McpServer.Domain.Settings;

/// <summary>
/// Resource-server config for validating the caller's own long-lived MCP access token
/// (minted via ArbiScannerWeb's "MCP Access" page — see
/// ArbiScannerWeb.Infrastructure.Services.McpTokenService, and the arbiscanner-mcp
/// client's access.token.lifespan override in keycloak/realm-export). This server is
/// multi-tenant by pass-through: the same validated token is forwarded as-is to
/// ArbiScannerWeb.API (it already carries that audience too), so there is no separate
/// exchange or credential minted by this server itself.
/// </summary>
public class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>
    /// The Keycloak realm issuer used for JWKS discovery, e.g.
    /// https://auth.arbiscannerwebapp.site/realms/arbiscanner-web
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>The expected audience claim on incoming access tokens — arbiscanner-mcp.</summary>
    public string Audience { get; set; } = string.Empty;
}
