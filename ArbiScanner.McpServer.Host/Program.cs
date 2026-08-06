using ArbiScanner.McpServer.Infrastructure;
using ArbiScanner.McpServer.Infrastructure.Authentication;
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
    builder.Services.AddOutboundHttpClients(builder.Configuration);
    builder.Services.AddMcpServices();

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    // No specific checks — this service holds no server-wide credential to validate
    // anymore (see McpAuthenticationMiddleware); /health is bare liveness.
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

    // MCP tool calls (SpreadTools) always require a bearer token that exchanges
    // successfully against Keycloak — see McpAuthenticationMiddleware and
    // KeycloakRefreshTokenProvider. Scoped to /mcp only so /health stays open for
    // Docker healthchecks with no credential.
    app.UseWhen(
        ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
        branch => branch.UseMiddleware<McpAuthenticationMiddleware>());

    // Explicit "/mcp" (rather than the SDK's root-mapped default) so this matches
    // regardless of whether the caller reaches this container directly
    // (docker-compose.local.yml, port 8087) or through the nginx /mcp/ route, which
    // preserves the path rather than stripping it — see nginx/nginx.conf.
    app.MapMcp("/mcp");
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
