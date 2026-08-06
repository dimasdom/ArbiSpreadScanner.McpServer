using ArbiScanner.McpServer.Abstractions.Interfaces;
using ArbiScanner.McpServer.Domain.Settings;
using ArbiScanner.McpServer.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArbiScanner.McpServer.Infrastructure;

public static class StartupSetup
{
    public static void AddMcpServices(this IServiceCollection services) =>
        services
            .AddMemoryCache()
            .AddSingleton<IOutboundTokenProvider, KeycloakRefreshTokenProvider>()
            .AddScoped<ISpreadsApiClient, SpreadsApiClient>();

    public static void AddKeycloakOptions(this IServiceCollection services, IConfiguration configuration) =>
        services
            .Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.SectionName))
            .Configure<WebApiOptions>(configuration.GetSection(WebApiOptions.SectionName));

    public static void AddOutboundHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient("WebApi", (sp, client) =>
        {
            var options = configuration.GetSection(WebApiOptions.SectionName).Get<WebApiOptions>()
                ?? throw new InvalidOperationException("WebApi options not configured");
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Token endpoint for the refresh-token grant (arbiscanner-mcp client, arbiscanner-web
        // realm) — base address is the realm issuer, so relative POSTs resolve to
        // .../protocol/openid-connect/token.
        services.AddHttpClient("KeycloakOutbound", (sp, client) =>
        {
            var options = configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
                ?? throw new InvalidOperationException("Keycloak options not configured");
            client.BaseAddress = new Uri(options.Authority.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }
}
