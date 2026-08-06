using ArbiScanner.McpServer.Infrastructure;
using ArbiScanner.McpServer.Infrastructure.HealthChecks;
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

    builder.Services.AddOutboundOptions(builder.Configuration);
    builder.Services.AddOutboundHttpClients(builder.Configuration);
    builder.Services.AddMcpServices();
    builder.Services.AddInboundAuthenticationJwt(builder.Configuration, builder.Environment);
    builder.Services.AddAuthorization();

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    builder.Services.AddHealthChecks()
        .AddCheck<OutboundTokenHealthCheck>("outbound-token");

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

    // MCP tool calls (SpreadTools) always require a valid inbound Keycloak bearer token
    // — see ArbiScanner.McpServer.Infrastructure.StartupSetup.AddInboundAuthenticationJwt.
    // This is purely a "may this caller hit the server" gate; the caller's identity is
    // not used for data scoping (see OutboundKeycloakOptions).
    app.MapMcp().RequireAuthorization();
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
