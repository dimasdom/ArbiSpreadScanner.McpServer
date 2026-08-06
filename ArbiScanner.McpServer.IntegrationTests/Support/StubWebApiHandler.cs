namespace ArbiScanner.McpServer.IntegrationTests.Support;

// Stands in for ArbiScannerWeb.API — a real instance isn't hosted in this project
// (it's a sibling submodule with its own integration test suite, see
// ArbiScannerWeb.IntegrationTests/Api/TradeOpportunityControllerTests.cs). What
// matters here is proving THIS server's own pipeline: DI wiring, MapMcp/tool
// registration, and that SpreadsApiClient forwards the caller's bearer token as-is.
public sealed class StubWebApiHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage>? OnRequest { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(OnRequest?.Invoke(request) ?? new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
