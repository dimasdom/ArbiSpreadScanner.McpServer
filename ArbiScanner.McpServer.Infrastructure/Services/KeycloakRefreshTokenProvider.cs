using System.Net.Http.Json;
using System.Text.Json;
using ArbiScanner.McpServer.Abstractions.Interfaces;
using ArbiScanner.McpServer.Domain.Settings;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArbiScanner.McpServer.Infrastructure.Services;

public sealed class KeycloakRefreshTokenProvider : IOutboundTokenProvider
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);

    private readonly HttpClient _tokenHttpClient;
    private readonly OutboundKeycloakOptions _options;
    private readonly ILogger<KeycloakRefreshTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedAccessToken;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

    public KeycloakRefreshTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<OutboundKeycloakOptions> options,
        ILogger<KeycloakRefreshTokenProvider> logger)
    {
        _tokenHttpClient = httpClientFactory.CreateClient("KeycloakOutbound");
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        if (!forceRefresh && IsCacheValid())
            return Result.Ok(_cachedAccessToken!);

        return await RefreshAsync(forceRefresh, cancellationToken);
    }

    private bool IsCacheValid() =>
        _cachedAccessToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc - RefreshBuffer;

    private async Task<Result<string>> RefreshAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring the lock — another concurrent tool call may
            // have already refreshed while this one was waiting. Skipped when
            // forceRefresh is set (e.g. after a downstream 401): that caller has already
            // ruled the cached token unusable, so a stale-but-"valid" cache entry must
            // not be handed back here.
            if (!forceRefresh && IsCacheValid())
                return Result.Ok(_cachedAccessToken!);

            // grant_type=refresh_token against the arbiscanner-mcp client's token endpoint.
            // The SAME configured refresh token is reused every time — safe because the
            // arbiscanner-web realm does not rotate/single-use refresh tokens by default.
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = _options.RefreshToken,
            });

            var response = await _tokenHttpClient.PostAsync("protocol/openid-connect/token", form, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Keycloak refresh-token grant failed with {StatusCode}: {Body}",
                    response.StatusCode, body);
                return Result.Fail<string>($"Keycloak refresh failed: {response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var token = payload.GetProperty("access_token").GetString();
            if (string.IsNullOrEmpty(token))
                return Result.Fail<string>("Keycloak token response did not contain an access_token");

            var expiresIn = payload.TryGetProperty("expires_in", out var expiresInElement)
                ? expiresInElement.GetInt32()
                : 300;

            _cachedAccessToken = token;
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return Result.Ok(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to obtain outbound access token from Keycloak");
            return Result.Fail<string>($"Error refreshing outbound token: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }
}
