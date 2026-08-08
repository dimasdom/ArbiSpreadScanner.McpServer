namespace ArbiScanner.McpServer.Domain.Dtos;

/// <summary>Mirrors ArbiScannerWeb.API's SpreadAnalysisDto - a server-computed verdict on
/// whether a spread is currently worth taking.</summary>
public class SpreadAnalysisDto
{
    public bool Recommended { get; set; }
    public List<string> Reasons { get; set; } = [];
    public string? TrendWarning { get; set; }
}
