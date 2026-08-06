using System.Net;
using System.Net.Http.Headers;
using ArbiScanner.McpServer.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ArbiScanner.McpServer.IntegrationTests;

// Hosts the real ArbiScanner.McpServer app (real DI graph, real middleware pipeline,
// real MapMcp/tool registration) via WebApplicationFactory, with only the downstream
// ArbiScannerWeb.API call stubbed (StubWebApiHandler) — everything else, including JWT
// bearer validation and the actual MCP Streamable HTTP protocol, runs for real. Proves
// this server's own wiring; Standard Token Exchange against a real Keycloak is already
// covered by ArbiScannerWeb.IntegrationTests' McpTokenControllerTests.
public class McpEndpointTests : IClassFixture<McpServerWebApplicationFactory>
{
    private readonly McpServerWebApplicationFactory _factory;

    public McpEndpointTests(McpServerWebApplicationFactory factory)
    {
        // The factory (and its WebApiHandler) is shared across every test in this class
        // via IClassFixture — reset per-test state here rather than leaking recorded
        // requests or a previous test's OnRequest handler across test methods.
        _factory = factory;
        _factory.WebApiHandler.OnRequest = null;
        _factory.WebApiHandler.Requests.Clear();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private async Task<McpClient> ConnectAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/mcp") },
            httpClient,
            NullLoggerFactory.Instance);

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken, loggerFactory: NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task Health_ReturnsOk_WithoutAuthentication()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Mcp_WithoutBearerToken_ConnectFailsAndNeverReachesWebApi()
    {
        var client = _factory.CreateClient();

        var act = async () => await ConnectAsync(client, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        _factory.WebApiHandler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Mcp_WithExpiredToken_ConnectFailsAndNeverReachesWebApi()
    {
        var client = _factory.CreateClient();
        var expiredToken = JwtTestTokenFactory.CreateToken("user-123", lifetime: TimeSpan.FromMinutes(-5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var act = async () => await ConnectAsync(client, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        _factory.WebApiHandler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CallTool_GetSpreadsForCurrentUser_ForwardsCallersOwnTokenAndReturnsData()
    {
        var token = JwtTestTokenFactory.CreateToken("user-123");
        string? forwardedAuthHeader = null;
        _factory.WebApiHandler.OnRequest = req =>
        {
            forwardedAuthHeader = req.Headers.Authorization?.Parameter;
            req.RequestUri!.AbsolutePath.Should().Be("/api/TradeOpportunity/GetSpreadsForUser");
            return JsonResponse(HttpStatusCode.OK, """
                {
                  "isSuccess": true,
                  "value": [
                    {
                      "positionModel": {
                        "symbol": "BTCUSDT",
                        "exchangeRateA": { "symbol": "BTCUSDT", "exchange": "Binance", "exchangeRate": 60000 },
                        "exchangeRateB": { "symbol": "BTCUSDT", "exchange": "Bybit", "exchangeRate": 60100 },
                        "exchangeShort": { "symbol": "BTCUSDT", "exchange": "Bybit", "exchangeRate": 60100 },
                        "exchangeLong": { "symbol": "BTCUSDT", "exchange": "Binance", "exchangeRate": 60000 }
                      },
                      "tickers": [],
                      "groupName": "BTC group"
                    }
                  ],
                  "message": ""
                }
                """);
        };

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var mcpClient = await ConnectAsync(client, CancellationToken.None);

        var result = await mcpClient.CallToolAsync(
            "get_spreads_for_current_user",
            new Dictionary<string, object?>(),
            cancellationToken: CancellationToken.None);

        (result.IsError ?? false).Should().BeFalse();
        var content = Assert.Single(result.Content.OfType<TextContentBlock>());
        content.Text.Should().Contain("BTCUSDT").And.Contain("BTC group");
        forwardedAuthHeader.Should().Be(token, "SpreadsApiClient forwards the caller's own token as-is, it does not mint a different one");
    }

    [Fact]
    public async Task CallTool_GetSpreadById_UrlEncodesIdAndSurfacesWebApiFailureAsToolText()
    {
        _factory.WebApiHandler.OnRequest = req =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/TradeOpportunity/GetSpreadInfo/abc%20123");
            return JsonResponse(HttpStatusCode.OK, """{"isSuccess":false,"value":null,"message":"Spread not found"}""");
        };

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokenFactory.CreateToken("user-123"));
        var mcpClient = await ConnectAsync(client, CancellationToken.None);

        var result = await mcpClient.CallToolAsync(
            "get_spread_by_id",
            new Dictionary<string, object?> { ["id"] = "abc 123" },
            cancellationToken: CancellationToken.None);

        var content = Assert.Single(result.Content.OfType<TextContentBlock>());
        content.Text.Should().Contain("Spread not found");
    }
}
