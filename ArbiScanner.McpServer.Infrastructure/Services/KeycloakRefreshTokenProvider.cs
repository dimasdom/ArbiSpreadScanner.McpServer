using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArbiScanner.McpServer.Abstractions.Interfaces;
using ArbiScanner.McpServer.Domain.Settings;
using FluentResults;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArbiScanner.McpServer.Infrastructure.Services;

/// <summary>
/// Exchanges a caller-presented refresh token for a short-lived access token via
/// Keycloak's refresh_token grant, caching per presented token (never the raw token
/// itself — see <see cref="ComputeCacheKey"/>). Single-instance in-process cache is
/// enough since this service always runs as one container, one process.
/// </summary>
public sealed class KeycloakRefreshTokenProvider : IOutboundTokenProvider
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);
    // Safety cap on how long a cache entry can live regardless of the access token's own
    // expiry — bounds memory growth from callers who stop presenting a token entirely.
    private static readonly TimeSpan MaxCacheEntryLifetime = TimeSpan.FromMinutes(20);

    private sealed record CacheEntry(string AccessToken, DateTimeOffset ExpiresAtUtc);

    private readonly HttpClient _tokenHttpClient;
    private readonly KeycloakOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<KeycloakRefreshTokenProvider> _logger;

    // One lock per presented token (keyed the same way as the cache) so concurrent tool
    // calls from the SAME caller don't each trigger their own Keycloak round-trip, without
    // serializing unrelated callers against each other. Fine to grow unbounded at this
    // service's scale (a handful of distinct callers) — not worth an eviction policy.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public KeycloakRefreshTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<KeycloakOptions> options,
        IMemoryCache cache,
        ILogger<KeycloakRefreshTokenProvider> logger)
    {
        _tokenHttpClient = httpClientFactory.CreateClient("KeycloakOutbound");
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<string>> GetAccessTokenAsync(
        string presentedRefreshToken, CancellationToken cancellationToken, bool forceRefresh = false)
    {
        var cacheKey = ComputeCacheKey(presentedRefreshToken);

        if (!forceRefresh && TryGetValid(cacheKey, out var cachedToken))
            return Result.Ok(cachedToken!);

        return await RefreshAsync(presentedRefreshToken, cacheKey, forceRefresh, cancellationToken);
    }

    private bool TryGetValid(string cacheKey, out string? accessToken)
    {
        if (_cache.TryGetValue(cacheKey, out CacheEntry? entry) && entry is not null
            && DateTimeOffset.UtcNow < entry.ExpiresAtUtc - RefreshBuffer)
        {
            accessToken = entry.AccessToken;
            return true;
        }

        accessToken = null;
        return false;
    }

    private async Task<Result<string>> RefreshAsync(
        string presentedRefreshToken, string cacheKey, bool forceRefresh, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring the lock — another concurrent tool call from
            // the same caller may have already refreshed while this one was waiting.
            // Skipped when forceRefresh is set (e.g. after a downstream 401): that caller
            // has already ruled the cached token unusable.
            if (!forceRefresh && TryGetValid(cacheKey, out var cachedToken))
                return Result.Ok(cachedToken!);

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = presentedRefreshToken,
            });

            var response = await _tokenHttpClient.PostAsync("protocol/openid-connect/token", form, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Keycloak refresh-token grant failed with {StatusCode}: {Body}",
                    response.StatusCode, body);
                return Result.Fail<string>($"Keycloak refresh failed: {response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var token = payload.TryGetProperty("access_token", out var tokenElement) ? tokenElement.GetString() : null;
            if (string.IsNullOrEmpty(token))
                return Result.Fail<string>("Keycloak token response did not contain an access_token");

            var expiresIn = payload.TryGetProperty("expires_in", out var expiresInElement)
                ? expiresInElement.GetInt32()
                : 300;

            var entry = new CacheEntry(token, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
            _cache.Set(cacheKey, entry, MaxCacheEntryLifetime);

            return Result.Ok(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to obtain outbound access token from Keycloak");
            return Result.Fail<string>($"Error refreshing outbound token: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    // Never use the raw refresh token as a cache key or log it — it's the caller's
    // standing bearer credential.
    private static string ComputeCacheKey(string presentedRefreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedRefreshToken));
        return Convert.ToHexString(hash);
    }
}
