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
    // camelCase to match ArbiScannerWeb.API's own JSON output (ASP.NET Core MVC's default) -
    // without this, tool JSON comes back PascalCase (e.g. "PositionModel" not
    // "positionModel"), silently inconsistent with every other JSON payload in this app.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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

    [McpServerTool(Name = "get_recommended_spreads")]
    [Description("Returns the top 2 recommended arbitrage spreads per category (Futures, Funding, Spot) among the current user's available spreads, already ranked by lowest combined funding+slippage cost among spreads with at least 1% spread. Use this for \"what should I look at\"/\"recommend a spread\" style questions.")]
    public async Task<string> GetRecommendedSpreads(CancellationToken cancellationToken)
    {
        var result = await _client.GetRecommendedSpreadsAsync(cancellationToken);
        return result.IsSuccess
            ? JsonSerializer.Serialize(result.Value, JsonOptions)
            : $"Error retrieving recommended spreads: {result.Errors.FirstOrDefault()?.Message}";
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
