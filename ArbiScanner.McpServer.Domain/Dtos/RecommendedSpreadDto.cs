namespace ArbiScanner.McpServer.Domain.Dtos;

/// <summary>Mirrors ArbiScannerWeb.API's RecommendedSpreadDto - one of the up-to-2-per-category
/// picks returned by get_recommended_spreads.</summary>
public class RecommendedSpreadDto
{
    public required SpreadDetailsDto Details { get; set; }
    public SpreadType Category { get; set; }
}
