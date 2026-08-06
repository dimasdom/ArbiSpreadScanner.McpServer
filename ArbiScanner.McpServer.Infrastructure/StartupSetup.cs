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
        services.AddScoped<ISpreadsApiClient, SpreadsApiClient>();

    public static void AddKeycloakOptions(this IServiceCollection services, IConfiguration configuration) =>
        services
            .Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.SectionName))
            .Configure<WebApiOptions>(configuration.GetSection(WebApiOptions.SectionName));

    public static void AddWebApiHttpClient(this IServiceCollection services, IConfiguration configuration) =>
        services.AddHttpClient("WebApi", (sp, client) =>
        {
            var options = configuration.GetSection(WebApiOptions.SectionName).Get<WebApiOptions>()
                ?? throw new InvalidOperationException("WebApi options not configured");
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

    // Pure resource-server config: no local Identity store or signing key — Keycloak
    // (arbiscanner-web realm) issues tokens, this just validates them against its
    // published JWKS via Authority-based discovery. Verbatim structural copy of
    // ArbiScannerWeb.Infrastructure.StartupSetup.AddAuthenticationJwt — deliberately not
    // a new invention. The validated token is then forwarded as-is to ArbiScannerWeb.API
    // by SpreadsApiClient, so no separate outbound credential exists in this service.
    public static void AddAuthenticationJwt(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            var jwtOptions = configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
                ?? throw new InvalidOperationException("Keycloak options not configured");

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
