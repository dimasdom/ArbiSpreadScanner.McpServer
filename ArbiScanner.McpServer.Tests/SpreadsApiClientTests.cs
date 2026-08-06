using System.Net;
using ArbiScanner.McpServer.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace ArbiScanner.McpServer.Tests;

public class SpreadsApiClientTests
{
    private const string PresentedToken = "callers-own-access-token";

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? OnRequest { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(OnRequest?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private readonly StubHttpMessageHandler _handler = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<ILogger<SpreadsApiClient>> _logger = new();

    public SpreadsApiClientTests()
    {
        _httpClientFactory.Setup(f => f.CreateClient("WebApi"))
            .Returns(() => new HttpClient(_handler) { BaseAddress = new Uri("http://web.local/") });
    }

    private static IHttpContextAccessor CreateContextAccessor(string? presentedToken = PresentedToken)
    {
        var context = new DefaultHttpContext();
        if (presentedToken is not null)
            context.Request.Headers.Authorization = new StringValues($"Bearer {presentedToken}");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(context);
        return accessor.Object;
    }

    private SpreadsApiClient CreateSut(string? presentedToken = PresentedToken) =>
        new(_httpClientFactory.Object, CreateContextAccessor(presentedToken), _logger.Object);

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task GetSpreadsForUserAsync_Success_ForwardsTheCallersOwnTokenAsIsAndReturnsValue()
    {
        _handler.OnRequest = req =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/TradeOpportunity/GetSpreadsForUser");
            req.Headers.Authorization!.Scheme.Should().Be("Bearer");
            req.Headers.Authorization!.Parameter.Should().Be(PresentedToken);
            return JsonResponse(HttpStatusCode.OK, """{"isSuccess":true,"value":[],"message":""}""");
        };

        var result = await CreateSut().GetSpreadsForUserAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSpreadByIdAsync_BuildsUrlWithEscapedId()
    {
        _handler.OnRequest = req =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/TradeOpportunity/GetSpreadInfo/abc%20123");
            return JsonResponse(HttpStatusCode.OK, """{"isSuccess":false,"message":"not found"}""");
        };

        var result = await CreateSut().GetSpreadByIdAsync("abc 123", CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Be("not found");
    }

    [Fact]
    public async Task GetSpreadsForUserAsync_EnvelopeIsSuccessFalse_ReturnsFailWithMessage()
    {
        _handler.OnRequest = _ => JsonResponse(HttpStatusCode.OK, """{"isSuccess":false,"message":"No active subscription"}""");

        var result = await CreateSut().GetSpreadsForUserAsync(CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Be("No active subscription");
    }

    [Fact]
    public async Task GetSpreadsForUserAsync_NonSuccessHttpStatus_ReturnsFail()
    {
        _handler.OnRequest = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var result = await CreateSut().GetSpreadsForUserAsync(CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("InternalServerError");
    }

    [Fact]
    public async Task GetSpreadsForUserAsync_WebApiRejectsTheToken_ReturnsFailWithoutRetrying()
    {
        // There is nothing to refresh — the caller's token is a fixed-lifetime credential
        // this service doesn't manage. A 401 from downstream just means it's invalid or
        // expired; the only remedy is generating a new one from the "MCP Access" page.
        _handler.OnRequest = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var result = await CreateSut().GetSpreadsForUserAsync(CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("Unauthorized");
        _handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSpreadsForUserAsync_NoInboundToken_ReturnsFailWithoutCallingWebApi()
    {
        var result = await CreateSut(presentedToken: null).GetSpreadsForUserAsync(CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("No inbound bearer token");
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSpreadsForUserAsync_MalformedJson_ReturnsFailAndLogs()
    {
        _handler.OnRequest = _ => JsonResponse(HttpStatusCode.OK, "not-json");

        var result = await CreateSut().GetSpreadsForUserAsync(CancellationToken.None);

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
