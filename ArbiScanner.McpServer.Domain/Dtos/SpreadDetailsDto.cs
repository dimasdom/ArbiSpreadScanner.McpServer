namespace ArbiScanner.McpServer.Domain.Dtos;

public class SpreadDetailsDto
{
    public required SpreadPositionDto PositionModel { get; set; }
    public required List<SpreadTickerDto> Tickers { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string? ShortExchangeUrl { get; set; }
    public string? LongExchangeUrl { get; set; }
}
