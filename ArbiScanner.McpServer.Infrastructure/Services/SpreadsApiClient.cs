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

/// <summary>
/// Calls ArbiScannerWeb.API on behalf of whichever account the current request's own
/// bearer token belongs to. There is no separate outbound credential: the inbound
/// token (already validated by AddJwtBearer before any tool call could reach here) is
/// forwarded as-is, because it already carries the arbiscanner-web-spa audience
/// ArbiScannerWeb.API itself validates — see McpTokenService on the web app side.
/// </summary>
public sealed class SpreadsApiClient : ISpreadsApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SpreadsApiClient> _logger;

    public SpreadsApiClient(
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SpreadsApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("WebApi");
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public Task<Result<List<SpreadDetailsDto>>> GetSpreadsForUserAsync(CancellationToken cancellationToken) =>
        GetAsync<List<SpreadDetailsDto>>("api/TradeOpportunity/GetSpreadsForUser", cancellationToken);

    public Task<Result<SpreadDetailsDto>> GetSpreadByIdAsync(string id, CancellationToken cancellationToken) =>
        GetAsync<SpreadDetailsDto>($"api/TradeOpportunity/GetSpreadInfo/{Uri.EscapeDataString(id)}", cancellationToken);

    public Task<Result<List<RecommendedSpreadDto>>> GetRecommendedSpreadsAsync(CancellationToken cancellationToken) =>
        GetAsync<List<RecommendedSpreadDto>>("api/TradeOpportunity/GetRecommendedSpreads", cancellationToken);

    private async Task<Result<T>> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var presentedToken = BearerTokenReader.Read(_httpContextAccessor.HttpContext);
        if (presentedToken is null)
            return Result.Fail<T>("No inbound bearer token present on the current request.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", presentedToken);
            var response = await _httpClient.SendAsync(request, cancellationToken);

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
}
