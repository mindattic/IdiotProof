// ============================================================================
// BreakoutPullbackStrategy - Generates "No Break, No Trade" strategies
// ============================================================================
//
// Based on the classic pro trader pattern:
// 1. Identify trigger (resistance level)
// 2. Wait for breakout
// 3. Wait for pullback to support/VWAP
// 4. Enter on confirmation (bounce from support)
// 5. Scale out at multiple targets
//
// This creates IdiotScript strategies that execute this pattern.
// ============================================================================

using IdiotProof.Shared;

namespace IdiotProof.Scripting;

/// <summary>
/// Builds breakout-pullback strategies from simple inputs.
/// </summary>
public sealed class BreakoutPullbackStrategyBuilder
{
    private string symbol = "";
    private string? name;
    private TradingSession session = TradingSession.RTH;
    private int quantity = 0;  // Auto-calculate based on price tier
    
    // Levels
    private double trigger;      // Breakout level (resistance)
    private double support;      // Must hold this after breakout
    private double invalidation; // Stop loss level
    private List<double> targets = [];
    
    // Pattern info
    private string bias = "Bullish continuation";
    private string pattern = "Breakout pullback";
    
    /// <summary>
    /// Creates a new breakout-pullback strategy for the given symbol.
    /// </summary>
    public static BreakoutPullbackStrategyBuilder Create(string symbol) => new() { symbol = symbol.ToUpperInvariant() };
    
    public BreakoutPullbackStrategyBuilder WithName(string name) { this.name = name; return this; }
    public BreakoutPullbackStrategyBuilder WithSession(TradingSession session) { this.session = session; return this; }
    public BreakoutPullbackStrategyBuilder WithQuantity(int qty) { quantity = qty; return this; }
    
    /// <summary>
    /// Sets the breakout trigger level (resistance that must break).
    /// </summary>
    public BreakoutPullbackStrategyBuilder Trigger(double price) { trigger = price; return this; }
    
    /// <summary>
    /// Sets the support level that must hold after breakout for confirmation.
    /// If not set, defaults to the trigger level (resistance becomes support).
    /// </summary>
    public BreakoutPullbackStrategyBuilder Support(double price) { support = price; return this; }
    
    /// <summary>
    /// Sets the invalidation level (where to stop out if pattern fails).
    /// If not set, calculated as 2% below support.
    /// </summary>
    public BreakoutPullbackStrategyBuilder Invalidation(double price) { invalidation = price; return this; }
    
    /// <summary>
    /// Adds a target price level. Call multiple times for T1, T2, T3, etc.
    /// </summary>
    public BreakoutPullbackStrategyBuilder Target(double price) { targets.Add(price); return this; }
    
    /// <summary>
    /// Adds multiple targets at once.
    /// </summary>
    public BreakoutPullbackStrategyBuilder Targets(params double[] prices) { targets.AddRange(prices); return this; }
    
    /// <summary>
    /// Sets the pattern description (e.g., "Coiled wedge breakout").
    /// </summary>
    public BreakoutPullbackStrategyBuilder Pattern(string pattern) { this.pattern = pattern; return this; }
    
    /// <summary>
    /// Sets the bias description (e.g., "Bullish continuation").
    /// </summary>
    public BreakoutPullbackStrategyBuilder Bias(string bias) { this.bias = bias; return this; }
    
    /// <summary>
    /// Auto-calculates targets based on support level and risk:reward ratios.
    /// </summary>
    /// <param name="riskReward1">R:R for T1 (default 1.5)</param>
    /// <param name="riskReward2">R:R for T2 (default 2.5)</param>
    /// <param name="riskReward3">R:R for T3 (default 4.0, optional)</param>
    public BreakoutPullbackStrategyBuilder AutoTargets(double riskReward1 = 1.5, double riskReward2 = 2.5, double riskReward3 = 0)
    {
        if (trigger <= 0) throw new InvalidOperationException("Set Trigger() before AutoTargets()");
        
        var effectiveSupport = this.support > 0 ? this.support : trigger;
        var effectiveInvalidation = this.invalidation > 0 ? this.invalidation : effectiveSupport * 0.98; // 2% below support

        // Entry is assumed near trigger level
        var entry = trigger;
        var risk = entry - effectiveInvalidation;

        targets.Clear();
        targets.Add(Math.Round(entry + risk * riskReward1, 2));  // T1
        targets.Add(Math.Round(entry + risk * riskReward2, 2));  // T2
        if (riskReward3 > 0)
            targets.Add(Math.Round(entry + risk * riskReward3, 2));  // T3

        return this;
    }

    /// <summary>
    /// Builds the strategy definition.
    /// </summary>
    public StrategyDefinition Build()
    {
        if (string.IsNullOrEmpty(symbol))
            throw new InvalidOperationException("Symbol is required");
        if (trigger <= 0)
            throw new InvalidOperationException("Trigger level is required");

        // Calculate defaults
        var effectiveSupport = this.support > 0 ? this.support : trigger;
        var effectiveInvalidation = this.invalidation > 0 ? this.invalidation : effectiveSupport * 0.98;
        var primaryTarget = targets.Count > 0 ? targets[0] : trigger * 1.15;
        
        var strategy = new StrategyDefinition
        {
            Symbol = symbol,
            Name = name ?? $"{symbol} Breakout-Pullback",
            Session = session,
            Quantity = quantity,
            Direction = TradeDirection.Long,
            TakeProfitPrice = primaryTarget,
            StopLossPrice = effectiveInvalidation
        };

        // Entry conditions: Breakout then Pullback then VWAP hold
        strategy.EntryConditions.Add(new PatternCondition(PatternType.Breakout, trigger));
        strategy.EntryConditions.Add(new PatternCondition(PatternType.Pullback, effectiveSupport));
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapAbove));
        
        return strategy;
    }
    
    /// <summary>
    /// Generates the IdiotScript representation.
    /// </summary>
    public string ToScript()
    {
        var effectiveSupport = this.support > 0 ? this.support : trigger;
        var effectiveInvalidation = this.invalidation > 0 ? this.invalidation : effectiveSupport * 0.98;
        var primaryTarget = targets.Count > 0 ? targets[0] : trigger * 1.15;

        var script = $@"// {symbol} - {bias}
// Pattern: {pattern}
// Trigger: Break over ${trigger:F2}
// Support: ${effectiveSupport:F2}
// Rule: NO BREAK, NO TRADE

Ticker({symbol})
    .Name(""{name ?? $"{symbol} Breakout-Pullback"}"")
    .Session(IS.{session.ToString().ToUpperInvariant()})
    .Breakout({trigger})
    .Pullback({effectiveSupport})
    .IsAboveVwap()
    .Long()
    .TakeProfit({primaryTarget})
    .StopLoss({effectiveInvalidation})";

        if (targets.Count > 1)
        {
            script += $"\n    // Additional targets: {string.Join(", ", targets.Skip(1).Select(t => $"${t:F2}"))}";
        }
        
        return script;
    }
    
    /// <summary>
    /// Generates a human-readable strategy card (like the trader's format).
    /// </summary>
    public string ToStrategyCard()
    {
        var effectiveSupport = this.support > 0 ? this.support : trigger;
        var effectiveInvalidation = this.invalidation > 0 ? this.invalidation : effectiveSupport * 0.98;

        var card = $@"
╔═══════════════════════════════════════════════════════════════════════════════╗
║  {symbol,-10} {bias,-30}                            ║
╠═══════════════════════════════════════════════════════════════════════════════╣
║  Pattern: {pattern,-60}  ║
║                                                                               ║
║  Trigger:      Break over ${trigger,-8:F2}                                     ║
║                                                                               ║
║  Confirmation:                                                                ║
║    • Pullback after breakout                                                  ║
║    • Holds / reclaims VWAP                                                    ║
║    • Holds above ${effectiveSupport,-8:F2}                                               ║
║                                                                               ║
║  Entry:        On confirmed VWAP hold after breakout                          ║
║                                                                               ║
║  Targets:";

        for (int i = 0; i < targets.Count; i++)
        {
            var pct = ((targets[i] - trigger) / trigger * 100);
            card += $"\n║    T{i + 1}: ${targets[i],-8:F2} ({pct:+0.0}%)                                              ";
        }
        
        card += $@"
║                                                                               ║
║  Invalidation:                                                                ║
║    • No break over ${trigger:F2}                                                ║
║    • Loss of VWAP and ${effectiveSupport:F2} after breakout                              ║
║                                                                               ║
║  Rule: NO BREAK, NO TRADE.                                                    ║
╚═══════════════════════════════════════════════════════════════════════════════╝";
        
        return card;
    }
}

/// <summary>
/// Represents a complete trading setup ready for execution.
/// </summary>
public sealed class TradingSetup
{
    public string Symbol { get; set; } = "";
    public string Bias { get; set; } = "";
    public string Pattern { get; set; } = "";
    
    // Levels
    public double TriggerPrice { get; set; }
    public double SupportPrice { get; set; }
    public double InvalidationPrice { get; set; }
    public List<double> TargetPrices { get; set; } = [];
    
    // Status
    public SetupStatus Status { get; set; } = SetupStatus.Watching;
    public DateTime? BreakoutTime { get; set; }
    public DateTime? EntryTime { get; set; }
    public double? EntryPrice { get; set; }
    
    // Risk calculations
    public double RiskPercent => SupportPrice > 0 ? (TriggerPrice - InvalidationPrice) / TriggerPrice * 100 : 0;
    public double RewardPercent => TargetPrices.Count > 0 ? (TargetPrices[0] - TriggerPrice) / TriggerPrice * 100 : 0;
    public double RiskRewardRatio => RiskPercent > 0 ? RewardPercent / RiskPercent : 0;
}

public enum SetupStatus
{
    Watching,       // Waiting for trigger
    Triggered,      // Breakout occurred
    PullingBack,    // In pullback phase
    Confirmed,      // Pullback held - ready to enter
    Entered,        // Position active
    HitT1,          // First target hit
    HitT2,          // Second target hit
    Completed,      // All targets hit or exited
    Invalidated     // Pattern failed
}
