namespace ArbiScanner.McpServer.Domain.Dtos;

public class SpreadDetailsDto
{
    public required SpreadPositionDto PositionModel { get; set; }
    public required List<SpreadTickerDto> Tickers { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string? ShortExchangeUrl { get; set; }
    public string? LongExchangeUrl { get; set; }

    /// <summary>Only populated for get_spread_by_id (needs ticker history) - null for
    /// get_spreads_for_current_user / get_recommended_spreads.</summary>
    public SpreadAnalysisDto? Analysis { get; set; }
}
