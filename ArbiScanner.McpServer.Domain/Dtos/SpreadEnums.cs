namespace ArbiScanner.McpServer.Domain.Dtos;

public enum SpreadType
{
    Futures,
    Funding,
    Spot
}

public enum MarketPositionAction
{
    Open,
    Update,
    Close
}

public enum OrderStatus
{
    Open,
    Closed
}
