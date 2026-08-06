using ArbiScanner.McpServer.Abstractions.Interfaces;
using Microsoft.AspNetCore.Http;

namespace ArbiScanner.McpServer.Infrastructure.Authentication;

/// <summary>
/// Gates access to the MCP endpoint: the presented bearer token is treated as a
/// long-lived Keycloak refresh token (see KeycloakOptions) and exchanged for a short-lived
/// access token. Success is what proves the caller is authenticated — there is no
/// separate JWT validation step, because the "token" here isn't a genuine access token,
/// it's a personal credential minted via ArbiScannerWeb's "MCP Access" page.
/// </summary>
public sealed class McpAuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    public McpAuthenticationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IOutboundTokenProvider tokenProvider)
    {
        var presentedToken = BearerTokenReader.Read(context);
        if (presentedToken is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing bearer token.");
            return;
        }

        var result = await tokenProvider.GetAccessTokenAsync(presentedToken, context.RequestAborted);
        if (result.IsFailed)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid or expired token.");
            return;
        }

        await _next(context);
    }
}
