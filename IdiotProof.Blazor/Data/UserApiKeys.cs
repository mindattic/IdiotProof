namespace IdiotProof.Blazor.Data;

/// <summary>
/// Per-user broker and data feed credentials. Sensitive string fields are stored
/// encrypted by UserKeyService using IDataProtector before being persisted.
/// </summary>
public sealed class UserApiKeys
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";

    // Alpaca
    public string? AlpacaApiKeyId { get; set; }
    public string? AlpacaApiSecretKey { get; set; }
    public bool AlpacaIsPaper { get; set; } = true;

    // Polygon
    public string? PolygonApiKey { get; set; }

    // Claude / LLM
    public string? ClaudeApiKey { get; set; }
    public bool LlmVotingEnabled { get; set; }
    public string ClaudeModel { get; set; } = "claude-sonnet-4-6";

    // Default broker/feed preferences
    public string DefaultBroker { get; set; } = "Sandbox";
    public string DefaultDataFeed { get; set; } = "Mock";
}
