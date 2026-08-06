using System.Net;
using ArbiScanner.McpServer.Domain.Settings;
using ArbiScanner.McpServer.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ArbiScanner.McpServer.Tests;

public class KeycloakRefreshTokenProviderTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => _callCount;
        public Func<HttpRequestMessage, HttpResponseMessage>? OnRequest { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(OnRequest?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private readonly StubHttpMessageHandler _handler = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<ILogger<KeycloakRefreshTokenProvider>> _logger = new();
    private readonly OutboundKeycloakOptions _options = new()
    {
        Authority = "http://keycloak.local/realms/arbiscanner-web",
        ClientId = "arbiscanner-mcp",
        ClientSecret = "secret",
        RefreshToken = "configured-refresh-token",
    };

    public KeycloakRefreshTokenProviderTests()
    {
        _httpClientFactory.Setup(f => f.CreateClient("KeycloakOutbound"))
            .Returns(() => new HttpClient(_handler) { BaseAddress = new Uri("http://keycloak.local/realms/arbiscanner-web/") });
    }

    private KeycloakRefreshTokenProvider CreateSut() =>
        new(_httpClientFactory.Object, Options.Create(_options), _logger.Object);

    private static HttpResponseMessage TokenResponse(string token, int expiresIn = 300) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($$"""{"access_token":"{{token}}","expires_in":{{expiresIn}},"token_type":"Bearer"}""")
    };

    [Fact]
    public async Task GetAccessTokenAsync_NoCache_RefreshesAndReturnsToken()
    {
        _handler.OnRequest = req =>
        {
            req.RequestUri!.AbsolutePath.Should().Contain("protocol/openid-connect/token");
            return TokenResponse("fresh-token");
        };

        var result = await CreateSut().GetAccessTokenAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("fresh-token");
        _handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachedTokenStillValid_DoesNotCallKeycloakAgain()
    {
        _handler.OnRequest = _ => TokenResponse("fresh-token", expiresIn: 900);
        var sut = CreateSut();
        await sut.GetAccessTokenAsync(CancellationToken.None);

        var result = await sut.GetAccessTokenAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("fresh-token");
        _handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachedTokenWithinRefreshBuffer_RefreshesAgain()
    {
        // 20s expiry is inside the provider's 30s refresh buffer — must be treated as stale.
        _handler.OnRequest = _ => TokenResponse("short-lived-token", expiresIn: 20);
        var sut = CreateSut();
        await sut.GetAccessTokenAsync(CancellationToken.None);

        await sut.GetAccessTokenAsync(CancellationToken.None);

        _handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ForceRefresh_SkipsCacheEvenWhenValid()
    {
        _handler.OnRequest = _ => TokenResponse("fresh-token", expiresIn: 900);
        var sut = CreateSut();
        await sut.GetAccessTokenAsync(CancellationToken.None);

        await sut.GetAccessTokenAsync(CancellationToken.None, forceRefresh: true);

        _handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ConcurrentCallsWithNoCache_OnlyRefreshesOnce()
    {
        _handler.OnRequest = _ => TokenResponse("fresh-token", expiresIn: 900);
        var sut = CreateSut();

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => sut.GetAccessTokenAsync(CancellationToken.None)));

        results.Should().OnlyContain(r => r.IsSuccess && r.Value == "fresh-token");
        _handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAccessTokenAsync_KeycloakReturnsNonSuccess_ReturnsFail()
    {
        _handler.OnRequest = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("invalid_grant")
        };

        var result = await CreateSut().GetAccessTokenAsync(CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task GetAccessTokenAsync_ResponseMissingAccessToken_ReturnsFail()
    {
        _handler.OnRequest = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"expires_in":300}""")
        };

        var result = await CreateSut().GetAccessTokenAsync(CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task GetAccessTokenAsync_KeycloakThrows_ReturnsFailAndLogs()
    {
        _handler.OnRequest = _ => throw new HttpRequestException("network down");

        var result = await CreateSut().GetAccessTokenAsync(CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        _logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
