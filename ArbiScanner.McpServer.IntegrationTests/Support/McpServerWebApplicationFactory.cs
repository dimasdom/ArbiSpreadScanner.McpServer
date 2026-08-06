using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace ArbiScanner.McpServer.IntegrationTests.Support;

public sealed class McpServerWebApplicationFactory : WebApplicationFactory<Program>
{
    public StubWebApiHandler WebApiHandler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>(JwtTestSettings.ConfigOverrides)
            {
                ["WebApi:BaseUrl"] = "http://web-api.local",
                ["Observability:Enabled"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            JwtTestSettings.ConfigureTestJwtBearer(services);

            // Replaces AddWebApiHttpClient's real handler with the stub — this is the
            // last "WebApi" client registration, and IHttpClientFactory applies
            // ConfigurePrimaryHttpMessageHandler registrations in order, so this one
            // wins. See ArbiScanner.McpServer.Infrastructure.StartupSetup.AddWebApiHttpClient.
            services.AddHttpClient("WebApi")
                .ConfigurePrimaryHttpMessageHandler(() => WebApiHandler);
        });
    }
}
