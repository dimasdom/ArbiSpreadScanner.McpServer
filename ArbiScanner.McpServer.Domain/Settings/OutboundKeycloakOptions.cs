namespace ArbiScanner.McpServer.Domain.Settings;

/// <summary>
/// Refresh-token grant config used to obtain short-lived access tokens for calling
/// ArbiScannerWeb.API. This server is single-tenant: the refresh token belongs to one
/// fixed ArbiScanner account, and every tool call acts on behalf of that account
/// regardless of which MCP client is calling in. See <see cref="InboundJwtOptions"/> for
/// the separate, unrelated inbound-caller gate.
/// </summary>
public class OutboundKeycloakOptions
{
    public const string SectionName = "OutboundKeycloak";

    /// <summary>
    /// The Keycloak realm issuer, e.g. https://auth.arbiscannerwebapp.site/realms/arbiscanner-web
    /// — the token endpoint is resolved as {Authority}/protocol/openid-connect/token.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>The arbiscanner-mcp confidential client id.</summary>
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// A long-lived Keycloak refresh token (obtained with scope=offline_access so it
    /// survives the realm's SSO session idle timeout) for the one ArbiScanner account
    /// this server acts as.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;
}
