using IdiotProof.Models;

namespace IdiotProof.Engine.Workspace;

/// <summary>
/// Persisted configuration for one workspace tab.
/// Each tab is an independent strategy workspace.
/// </summary>
public sealed class WorkspaceTab
{
    public string TabId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "New Workspace";
    public int DisplayOrder { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Symbols to watch/trade in this workspace.</summary>
    public List<string> Watchlist { get; set; } = [];

    /// <summary>Strategies bound to this workspace.</summary>
    public List<StrategyBinding> Strategies { get; set; } = [];

    /// <summary>Override the global broker for this tab. Null = use global default.</summary>
    public string? BrokerOverride { get; set; }

    /// <summary>Override the global data feed for this tab. Null = use global default.</summary>
    public string? DataFeedOverride { get; set; }

    /// <summary>Per-tab settings for risk management and sessions.</summary>
    public TabSettings Settings { get; set; } = new();
}

/// <summary>
/// Binds a strategy type with parameters to a workspace tab.
/// </summary>
public sealed class StrategyBinding
{
    public string StrategyName { get; set; } = "ITI";
    public Dictionary<string, object> Parameters { get; set; } = new();
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Per-tab settings for trading behavior.
/// </summary>
public sealed class TabSettings
{
    public bool AutoTrade { get; set; }
    public decimal MaxPositionSize { get; set; } = 5000m;
    public TradingSession AllowedSessions { get; set; } = TradingSession.RTH;
    public RiskLimits RiskLimits { get; set; } = new();
}
