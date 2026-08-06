using System.ComponentModel;
using System.Text.Json;
using ArbiScanner.McpServer.Abstractions.Interfaces;
using ModelContextProtocol.Server;

namespace ArbiScanner.McpServer.Host.Tools;

/// <summary>
/// MCP tools wrapping ArbiScannerWeb.API's TradeOpportunityController. Both tools act on
/// behalf of whichever ArbiScanner account the caller's own bearer token belongs to —
/// see Infrastructure.StartupSetup.AddAuthenticationJwt and SpreadsApiClient.
/// </summary>
[McpServerToolType]
public sealed class SpreadTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ISpreadsApiClient _client;

    public SpreadTools(ISpreadsApiClient client) => _client = client;

    [McpServerTool(Name = "get_spreads_for_current_user")]
    [Description("Returns all currently-open arbitrage spread opportunities matching the configured ArbiScanner account's settings filters.")]
    public async Task<string> GetSpreadsForCurrentUser(CancellationToken cancellationToken)
    {
        var result = await _client.GetSpreadsForUserAsync(cancellationToken);
        return result.IsSuccess
            ? JsonSerializer.Serialize(result.Value, JsonOptions)
            : $"Error retrieving spreads: {result.Errors.FirstOrDefault()?.Message}";
    }

    [McpServerTool(Name = "get_spread_by_id")]
    [Description("Returns full details, including ticker history, for one arbitrage spread opportunity by id.")]
    public async Task<string> GetSpreadById(
        [Description("The spread opportunity id (GUID)")] string id,
        CancellationToken cancellationToken)
    {
        var result = await _client.GetSpreadByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? JsonSerializer.Serialize(result.Value, JsonOptions)
            : $"Error retrieving spread {id}: {result.Errors.FirstOrDefault()?.Message}";
    }
}
