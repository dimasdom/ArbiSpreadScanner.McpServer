using FluentResults;

namespace ArbiScanner.McpServer.Abstractions.Interfaces;

/// <summary>Exchanges the configured Keycloak refresh token for a short-lived access
/// token, caching and auto-refreshing it. See OutboundKeycloakOptions.</summary>
public interface IOutboundTokenProvider
{
    /// <param name="forceRefresh">Skip the cache and refresh unconditionally — used after a
    /// downstream 401 to rule out a stale/revoked cached token before giving up.</param>
    Task<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false);
}
