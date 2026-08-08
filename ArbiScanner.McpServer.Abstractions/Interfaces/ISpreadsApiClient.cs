using ArbiScanner.McpServer.Domain.Dtos;
using FluentResults;

namespace ArbiScanner.McpServer.Abstractions.Interfaces;

/// <summary>Calls ArbiScannerWeb.API's TradeOpportunityController on behalf of whichever
/// ArbiScanner account the current request's own bearer token belongs to (see
/// KeycloakOptions — the token is forwarded as-is, not exchanged for a different one).</summary>
public interface ISpreadsApiClient
{
    Task<Result<List<SpreadDetailsDto>>> GetSpreadsForUserAsync(CancellationToken cancellationToken);

    Task<Result<SpreadDetailsDto>> GetSpreadByIdAsync(string id, CancellationToken cancellationToken);

    Task<Result<List<RecommendedSpreadDto>>> GetRecommendedSpreadsAsync(CancellationToken cancellationToken);
}
