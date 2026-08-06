namespace ArbiScanner.McpServer.Domain.Settings;

/// <summary>
/// Config for the arbiscanner-mcp confidential client. This server is multi-tenant by
/// pass-through: the caller presents their own long-lived offline_access refresh token
/// (obtained from ArbiScannerWeb's "MCP Access" page — see
/// ArbiScannerWeb.Infrastructure.Services.McpTokenService) as their bearer credential.
/// This client's id/secret is used to exchange that presented refresh token for a
/// short-lived access token, which doubles as both the "is this caller allowed in"
/// check and the outbound Bearer used to call ArbiScannerWeb.API on their behalf.
/// </summary>
public class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>
    /// The Keycloak realm issuer, e.g. https://auth.arbiscannerwebapp.site/realms/arbiscanner-web
    /// — the token endpoint is resolved as {Authority}/protocol/openid-connect/token.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>The arbiscanner-mcp confidential client id.</summary>
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
