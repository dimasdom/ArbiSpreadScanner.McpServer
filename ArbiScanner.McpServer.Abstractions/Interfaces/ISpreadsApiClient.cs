using ArbiScanner.McpServer.Domain.Dtos;
using FluentResults;

namespace ArbiScanner.McpServer.Abstractions.Interfaces;

/// <summary>Calls ArbiScannerWeb.API's TradeOpportunityController on behalf of whichever
/// ArbiScanner account the current request's presented token belongs to (see
/// KeycloakOptions and Authentication.McpAuthenticationMiddleware).</summary>
public interface ISpreadsApiClient
{
    Task<Result<List<SpreadDetailsDto>>> GetSpreadsForUserAsync(CancellationToken cancellationToken);

    Task<Result<SpreadDetailsDto>> GetSpreadByIdAsync(string id, CancellationToken cancellationToken);
}
