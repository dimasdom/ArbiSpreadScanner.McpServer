using FluentResults;

namespace ArbiScanner.McpServer.Abstractions.Interfaces;

/// <summary>
/// Exchanges a caller-presented Keycloak refresh token for a short-lived access token,
/// caching per presented token and auto-refreshing near expiry. See KeycloakOptions.
/// A successful exchange doubles as proof the caller is authenticated — there is no
/// separate inbound validation step.
/// </summary>
public interface IOutboundTokenProvider
{
    /// <param name="presentedRefreshToken">The long-lived offline_access refresh token the
    /// caller presented as their bearer credential.</param>
    /// <param name="forceRefresh">Skip the cache and refresh unconditionally — used after a
    /// downstream 401 to rule out a stale/revoked cached token before giving up.</param>
    Task<Result<string>> GetAccessTokenAsync(string presentedRefreshToken, CancellationToken cancellationToken, bool forceRefresh = false);
}
