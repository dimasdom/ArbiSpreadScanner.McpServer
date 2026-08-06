using Microsoft.AspNetCore.Http;

namespace ArbiScanner.McpServer.Infrastructure.Authentication;

/// <summary>Reads the raw bearer token string off the current request, shared by
/// <see cref="McpAuthenticationMiddleware"/> and <c>SpreadsApiClient</c> so both agree on
/// exactly what "the presented token" means.</summary>
public static class BearerTokenReader
{
    private const string BearerPrefix = "Bearer ";

    public static string? Read(HttpContext? context)
    {
        var header = context?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return header[BearerPrefix.Length..];
    }
}
