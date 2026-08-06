using ArbiScanner.McpServer.Infrastructure;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .Enrich.WithSpan();

        if (ctx.Configuration.GetValue("VerboseLogging:Enabled", false))
        {
            cfg.MinimumLevel.Debug();
        }
    });

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddKeycloakOptions(builder.Configuration);
    builder.Services.AddWebApiHttpClient(builder.Configuration);
    builder.Services.AddMcpServices();
    builder.Services.AddAuthenticationJwt(builder.Configuration, builder.Environment);
    builder.Services.AddAuthorization();

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    builder.Services.AddHealthChecks();

    var observabilityEnabled = builder.Configuration.GetValue("Observability:Enabled", true);
    if (observabilityEnabled)
    {
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("arbiscanner-mcp", serviceVersion: "1.0.0"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(o =>
                {
                    o.RecordException = true;
                    o.Filter = ctx => ctx.Request.Path != "/metrics" && ctx.Request.Path != "/health";
                })
                .AddHttpClientInstrumentation(o => o.RecordException = true)
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());
    }

    var app = builder.Build();

    if (observabilityEnabled)
    {
        app.UseOpenTelemetryPrometheusScrapingEndpoint();
    }

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
        app.UseHttpsRedirection();

    app.UseAuthentication();
    app.UseAuthorization();

    // Explicit "/mcp" (rather than the SDK's root-mapped default) so this matches
    // regardless of whether the caller reaches this container directly
    // (docker-compose.local.yml, port 8087) or through the nginx /mcp route, which
    // proxies the path through unchanged — see nginx/nginx.conf. Every MCP call
    // requires a valid Keycloak-issued bearer token (RequireAuthorization); SpreadsApiClient
    // then forwards that same token as-is to ArbiScannerWeb.API.
    app.MapMcp("/mcp").RequireAuthorization();
    app.MapHealthChecks("/health");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program
{
    protected Program() { }
}
