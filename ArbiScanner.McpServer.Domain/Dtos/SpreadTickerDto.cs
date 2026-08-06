namespace ArbiScanner.McpServer.Domain.Dtos;

public class SpreadTickerDto
{
    public Guid Guid { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public double Spread { get; set; }
    public string EchangeA { get; set; } = string.Empty;
    public string ExchangeB { get; set; } = string.Empty;
    public string ExchangeLong { get; set; } = string.Empty;
    public string ExchangeShort { get; set; } = string.Empty;
    public double RateA { get; set; }
    public double RateB { get; set; }
    public DateTime DateTime { get; set; }
    public List<OrderLevelDto>? BidsExchangeA { get; set; }
    public List<OrderLevelDto>? AsksExchangeA { get; set; }
    public List<OrderLevelDto>? BidsExchangeB { get; set; }
    public List<OrderLevelDto>? AsksExchangeB { get; set; }
}
