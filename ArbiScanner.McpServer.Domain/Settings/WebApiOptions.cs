namespace ArbiScanner.McpServer.Domain.Settings;

/// <summary>Location of ArbiScannerWeb.API, the service the two MCP tools call.</summary>
public class WebApiOptions
{
    public const string SectionName = "WebApi";

    /// <summary>e.g. http://web:8080 inside the Docker network, or a local dotnet-run URL.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}
