using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArbiScanner.McpServer.Abstractions.Interfaces;
using ArbiScanner.McpServer.Domain.Dtos;
using ArbiScanner.McpServer.Infrastructure.Authentication;
using FluentResults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ArbiScanner.McpServer.Infrastructure.Services;

public sealed class SpreadsApiClient : ISpreadsApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly IOutboundTokenProvider _tokenProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SpreadsApiClient> _logger;

    public SpreadsApiClient(
        IHttpClientFactory httpClientFactory,
        IOutboundTokenProvider tokenProvider,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SpreadsApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("WebApi");
        _tokenProvider = tokenProvider;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public Task<Result<List<SpreadDetailsDto>>> GetSpreadsForUserAsync(CancellationToken cancellationToken) =>
        GetAsync<List<SpreadDetailsDto>>("api/TradeOpportunity/GetSpreadsForUser", cancellationToken);

    public Task<Result<SpreadDetailsDto>> GetSpreadByIdAsync(string id, CancellationToken cancellationToken) =>
        GetAsync<SpreadDetailsDto>($"api/TradeOpportunity/GetSpreadInfo/{Uri.EscapeDataString(id)}", cancellationToken);

    private async Task<Result<T>> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAuthenticatedAsync(relativeUrl, cancellationToken);

            // Proactive refresh keeps the cached token valid in the common case, but the
            // cache can still go stale out-of-band (clock skew, the account's session
            // revoked server-side) — force one refresh and retry once before giving up.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response = await SendAuthenticatedAsync(relativeUrl, cancellationToken, forceRefresh: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "ArbiScannerWeb.API call to {Url} failed with {StatusCode}: {Body}",
                    relativeUrl, response.StatusCode, body);
                return Result.Fail<T>($"ArbiScannerWeb.API returned {response.StatusCode}");
            }

            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelopeDto<T>>(JsonOptions, cancellationToken);
            if (envelope is null)
                return Result.Fail<T>("Failed to deserialize ArbiScannerWeb.API response");

            if (!envelope.IsSuccess)
                return Result.Fail<T>(string.IsNullOrEmpty(envelope.Message) ? "Request failed" : envelope.Message);

            return Result.Ok(envelope.Value!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling ArbiScannerWeb.API {Url}", relativeUrl);
            return Result.Fail<T>($"Error calling ArbiScannerWeb.API: {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        string relativeUrl, CancellationToken cancellationToken, bool forceRefresh = false)
    {
        // McpAuthenticationMiddleware already validated a bearer token is present on this
        // request before any tool call could reach here — this re-reads the same header
        // rather than threading the value through, and hits the cache
        // KeycloakRefreshTokenProvider already primed unless forceRefresh is set.
        var presentedToken = BearerTokenReader.Read(_httpContextAccessor.HttpContext)
            ?? throw new InvalidOperationException("No inbound bearer token present on the current request.");

        var tokenResult = await _tokenProvider.GetAccessTokenAsync(presentedToken, cancellationToken, forceRefresh);
        if (tokenResult.IsFailed)
        {
            throw new InvalidOperationException(
                $"Could not obtain an outbound access token: {tokenResult.Errors.FirstOrDefault()?.Message}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);
        return await _httpClient.SendAsync(request, cancellationToken);
    }
}
