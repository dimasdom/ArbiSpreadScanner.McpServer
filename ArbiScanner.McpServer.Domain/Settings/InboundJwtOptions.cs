namespace ArbiScanner.McpServer.Domain.Settings;

/// <summary>
/// Resource-server config for validating the calling MCP client's own Keycloak-issued
/// access token. This is a gate ("may this caller hit the server at all") — it is
/// deliberately separate from <see cref="OutboundKeycloakOptions"/>, which governs the
/// token this server uses to call ArbiScannerWeb.API on behalf of one fixed account.
/// </summary>
public class InboundJwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The Keycloak realm issuer used for JWKS discovery, e.g.
    /// https://auth.arbiscannerwebapp.site/realms/arbiscanner-web
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>The expected audience claim on incoming access tokens, e.g. arbiscanner-mcp.</summary>
    public string Audience { get; set; } = string.Empty;
}
