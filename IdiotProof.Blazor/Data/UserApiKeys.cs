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
    // broker/feed credential the editor needs. Paper and Live are DIFFERENT Alpaca
    // accounts with their own key pairs (not one pair + a flag) — a strategy's
    // BrokerMode picks which pair UserBrokerResolver uses.
    public string? AlpacaApiKeyId { get; set; }
    public string? AlpacaApiSecretKey { get; set; }
    public string? AlpacaLiveApiKeyId { get; set; }
    public string? AlpacaLiveApiSecretKey { get; set; }

    // Claude / LLM
    public string? ClaudeApiKey { get; set; }
    public bool LlmVotingEnabled { get; set; }
    public string ClaudeModel { get; set; } = "claude-sonnet-5";

    // Default broker/feed preferences
    public string DefaultBroker { get; set; } = "Sandbox";
    public string DefaultDataFeed { get; set; } = "Mock";

    // Alpaca OAuth (Connect API) — account-linking alternative to the raw
    // key/secret above (IP-A26). Stored encrypted like the other secrets.
    // DORMANT: obtained by the /connect/alpaca flow and stored, but not yet
    // routed through (trading still uses the key/secret pair until Bearer mode
    // is paper-verified).
    public string? AlpacaOAuthAccessToken { get; set; }
    public string? AlpacaOAuthRefreshToken { get; set; }
    public string? AlpacaOAuthScope { get; set; }
}
