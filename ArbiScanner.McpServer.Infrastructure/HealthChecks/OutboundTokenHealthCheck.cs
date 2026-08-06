using ArbiScanner.McpServer.Abstractions.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArbiScanner.McpServer.Infrastructure.HealthChecks;

/// <summary>Confirms the configured outbound refresh token is still exchangeable — the
/// single most useful health signal for a server that's otherwise stateless.</summary>
public sealed class OutboundTokenHealthCheck : IHealthCheck
{
    private readonly IOutboundTokenProvider _tokenProvider;

    public OutboundTokenHealthCheck(IOutboundTokenProvider tokenProvider) => _tokenProvider = tokenProvider;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var result = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        return result.IsSuccess
            ? HealthCheckResult.Healthy("Outbound Keycloak refresh token is valid")
            : HealthCheckResult.Unhealthy(
                $"Outbound Keycloak refresh token could not be exchanged: {result.Errors.FirstOrDefault()?.Message}");
    }
}
