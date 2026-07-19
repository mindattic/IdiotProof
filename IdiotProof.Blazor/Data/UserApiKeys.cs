namespace IdiotProof.Blazor.Data;

/// <summary>
/// Per-user broker and data feed credentials. Sensitive string fields are stored
/// encrypted by UserKeyService using IDataProtector before being persisted.
/// </summary>
public sealed class UserApiKeys
{
    public int Id { get; set; }
    public Guid UserId { get; set; }

    // Alpaca — provides both trading + real-time market data, so it's the only
    // broker/feed credential the editor needs.
    public string? AlpacaApiKeyId { get; set; }
    public string? AlpacaApiSecretKey { get; set; }
    public bool AlpacaIsPaper { get; set; } = true;

    // Claude / LLM
    public string? ClaudeApiKey { get; set; }
    public bool LlmVotingEnabled { get; set; }
    public string ClaudeModel { get; set; } = "claude-sonnet-5";

    // Polygon.io — historical market data for backtesting (IP-US-J1)
    public string? PolygonApiKey { get; set; }

    // Default broker/feed preferences
    public string DefaultBroker { get; set; } = "Sandbox";
    public string DefaultDataFeed { get; set; } = "Mock";
}
