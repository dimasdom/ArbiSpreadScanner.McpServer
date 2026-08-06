namespace ArbiScanner.McpServer.Domain.Dtos;

public class ExchangeRateDto
{
    public required string Symbol { get; set; }
    public required string Exchange { get; set; }
    public double ExchangeRate { get; set; }
    public double VolumeAsk { get; set; }
    public double VolumeBid { get; set; }
    public double SlippageLong { get; set; }
    public double SlippageShort { get; set; }
    public double SummarySlipage { get; set; }
    public double? FundingRateValue { get; set; }
}
