using ArbiScanner.McpServer.Domain.Dtos;
using FluentResults;

namespace ArbiScanner.McpServer.Abstractions.Interfaces;

/// <summary>Calls ArbiScannerWeb.API's TradeOpportunityController on behalf of the one
/// ArbiScanner account this server is configured for (see OutboundKeycloakOptions).</summary>
public interface ISpreadsApiClient
{
    Task<Result<List<SpreadDetailsDto>>> GetSpreadsForUserAsync(CancellationToken cancellationToken);

    Task<Result<SpreadDetailsDto>> GetSpreadByIdAsync(string id, CancellationToken cancellationToken);
}
