namespace ArbiScanner.McpServer.Domain.Dtos;

/// <summary>
/// Mirrors ArbiScannerWeb.Infrastructure.Extensions.SerializableResult&lt;T&gt; — the JSON
/// envelope every ArbiScannerWeb.API controller action returns.
/// </summary>
public class ApiEnvelopeDto<T>
{
    public bool IsSuccess { get; set; }
    public T? Value { get; set; }
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
}
