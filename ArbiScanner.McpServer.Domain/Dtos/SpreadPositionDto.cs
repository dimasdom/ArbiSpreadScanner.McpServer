namespace ArbiScanner.McpServer.Domain.Dtos;

public class SpreadPositionDto
{
    public Guid Guid { get; set; }
    public required ExchangeRateDto ExchangeRateA { get; set; }
    public required ExchangeRateDto ExchangeRateB { get; set; }
    public required ExchangeRateDto ExchangeShort { get; set; }
    public required ExchangeRateDto ExchangeLong { get; set; }
    public double SummaryTarrif { get; set; }
    public double PossibleProfit { get; set; }
    public double TotalFunding { get; set; }
    public double Spread { get; set; }
    public SpreadType Type { get; set; }
    public MarketPositionAction ActionType { get; set; }
    public double StartSpread { get; set; }
    public required string Symbol { get; set; }
    public List<OrderLevelDto>? BidsExchangeA { get; set; }
    public List<OrderLevelDto>? AsksExchangeA { get; set; }
    public List<OrderLevelDto>? BidsExchangeB { get; set; }
    public List<OrderLevelDto>? AsksExchangeB { get; set; }
    public DateTime DateTime { get; set; }
    public OrderStatus OrderStatus { get; set; } = OrderStatus.Open;
}
