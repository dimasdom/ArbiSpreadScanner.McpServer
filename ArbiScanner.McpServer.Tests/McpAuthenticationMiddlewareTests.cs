using ArbiScanner.McpServer.Abstractions.Interfaces;
using ArbiScanner.McpServer.Infrastructure.Authentication;
using FluentAssertions;
using FluentResults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace ArbiScanner.McpServer.Tests;

public class McpAuthenticationMiddlewareTests
{
    private readonly Mock<IOutboundTokenProvider> _tokenProvider = new();
    private bool _nextCalled;

    private McpAuthenticationMiddleware CreateSut() =>
        new(_ => { _nextCalled = true; return Task.CompletedTask; });

    private static DefaultHttpContext CreateContext(string? authorizationHeader)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        if (authorizationHeader is not null)
            context.Request.Headers.Authorization = new StringValues(authorizationHeader);
        return context;
    }

    [Fact]
    public async Task InvokeAsync_NoAuthorizationHeader_ReturnsUnauthorizedAndDoesNotCallNext()
    {
        var context = CreateContext(authorizationHeader: null);

        await CreateSut().InvokeAsync(context, _tokenProvider.Object);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        _nextCalled.Should().BeFalse();
        _tokenProvider.Verify(t => t.GetAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_NonBearerAuthorizationHeader_ReturnsUnauthorizedAndDoesNotCallNext()
    {
        var context = CreateContext(authorizationHeader: "Basic dXNlcjpwYXNz");

        await CreateSut().InvokeAsync(context, _tokenProvider.Object);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_TokenExchangeFails_ReturnsUnauthorizedAndDoesNotCallNext()
    {
        _tokenProvider.Setup(t => t.GetAccessTokenAsync("caller-token", It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Result.Fail<string>("invalid_grant"));
        var context = CreateContext(authorizationHeader: "Bearer caller-token");

        await CreateSut().InvokeAsync(context, _tokenProvider.Object);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_TokenExchangeSucceeds_CallsNext()
    {
        _tokenProvider.Setup(t => t.GetAccessTokenAsync("caller-token", It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Result.Ok("access-token"));
        var context = CreateContext(authorizationHeader: "Bearer caller-token");

        await CreateSut().InvokeAsync(context, _tokenProvider.Object);

        _nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
