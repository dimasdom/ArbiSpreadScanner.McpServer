using ArbiScanner.McpServer.Abstractions.Interfaces;
using ArbiScanner.McpServer.Domain.Settings;
using ArbiScanner.McpServer.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ArbiScanner.McpServer.Infrastructure;

public static class StartupSetup
{
    public static void AddMcpServices(this IServiceCollection services) =>
        services
            .AddSingleton<IOutboundTokenProvider, KeycloakRefreshTokenProvider>()
            .AddScoped<ISpreadsApiClient, SpreadsApiClient>();

    public static void AddOutboundOptions(this IServiceCollection services, IConfiguration configuration) =>
        services
            .Configure<InboundJwtOptions>(configuration.GetSection(InboundJwtOptions.SectionName))
            .Configure<OutboundKeycloakOptions>(configuration.GetSection(OutboundKeycloakOptions.SectionName))
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

        // Token endpoint for the outbound refresh-token grant (arbiscanner-mcp client,
        // arbiscanner-web realm) — base address is the realm issuer, so relative POSTs
        // resolve to .../protocol/openid-connect/token.
        services.AddHttpClient("KeycloakOutbound", (sp, client) =>
        {
            var options = configuration.GetSection(OutboundKeycloakOptions.SectionName).Get<OutboundKeycloakOptions>()
                ?? throw new InvalidOperationException("OutboundKeycloak options not configured");
            client.BaseAddress = new Uri(options.Authority.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }

    // Pure resource-server config for the INBOUND MCP caller gate: no local Identity
    // store or signing key — Keycloak (arbiscanner-web realm) issues tokens, this just
    // validates them against its published JWKS via Authority-based discovery. Verbatim
    // structural copy of ArbiScannerWeb.Infrastructure.StartupSetup.AddAuthenticationJwt —
    // deliberately not a new invention.
    public static void AddInboundAuthenticationJwt(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            var jwtOptions = configuration.GetSection(InboundJwtOptions.SectionName).Get<InboundJwtOptions>()
                ?? throw new InvalidOperationException("Jwt options not configured");

            options.Authority = jwtOptions.Authority;
            options.Audience = jwtOptions.Audience;
            // Only relaxed for local dev against a plain-HTTP Keycloak container (see
            // docker-compose.local.yml) — every real deployment keeps this true.
            options.RequireHttpsMetadata = !environment.IsDevelopment();
            // Without this, ASP.NET remaps short JWT claim names (e.g. "sub") to long
            // legacy URIs before TokenValidationParameters ever sees them.
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = "sub",
            };
        });
    }
}
