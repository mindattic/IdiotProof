// ============================================================================
// IdiotProof.Scripting - IdiotScript DSL for Trading Strategies
// ============================================================================
// This library provides the IdiotScript domain-specific language for
// defining trading strategies in a fluent, readable way.
//
// FUTURE VISION:
// - Visual strategy builder with drag-and-drop
// - Pattern recognition (breakouts, pullbacks, etc.)
// - Backtesting integration
// - Strategy sharing and templates
// ============================================================================

using IdiotProof.Models;
using IdiotProof.Shared;

namespace IdiotProof.Scripting;

/// <summary>
/// Entry point for building IdiotScript strategies.
/// </summary>
public static class Stock
{
    /// <summary>
    /// Starts building a strategy for the specified ticker.
    /// </summary>
    public static StrategyBuilder Ticker(string symbol) => new(symbol);
}

/// <summary>
/// Fluent builder for trading strategies.
/// </summary>
public sealed class StrategyBuilder
{
    private readonly StrategyDefinition strategy = new();
    
    internal StrategyBuilder(string symbol)
    {
        strategy.Symbol = symbol.ToUpperInvariant();
    }
    
    // ========================================
    // CONFIGURATION
    // ========================================
    
    public StrategyBuilder Name(string name)
    {
        strategy.Name = name;
        return this;
    }
    
    public StrategyBuilder Session(TradingSession session)
    {
        strategy.Session = session;
        return this;
    }
    
    /// <summary>
    /// Sizes the position by share count. Mutually exclusive with the decimal
    /// overload (notional). Calling this clears any prior notional setting.
    /// Example: <c>.Quantity(100)</c> = 100 shares.
    /// </summary>
    public StrategyBuilder Quantity(int shares)
    {
        strategy.Quantity = shares;
        strategy.NotionalAmount = null;
        return this;
    }

    /// <summary>
    /// Sizes the position by dollar amount (Alpaca's <c>notional</c> field).
    /// Mutually exclusive with the int overload. Useful for risk-budgeted
    /// strategies — "$1000 of TSLA" works regardless of share price.
    /// Example: <c>.Quantity(1000m)</c> = $1000 worth.
    /// </summary>
    public StrategyBuilder Quantity(decimal notionalDollars)
    {
        strategy.Quantity = 0;
        strategy.NotionalAmount = notionalDollars;
        return this;
    }

    /// <summary>
    /// Explicit shares-only setter. Same as <see cref="Quantity(int)"/> but
    /// reads more naturally inside Conditions/Branch builders that use
    /// <c>Quantity.Shares(N)</c> idiom.
    /// </summary>
    public StrategyBuilder QuantityShares(int shares) => Quantity(shares);

    /// <summary>
    /// Explicit notional-only setter. Same as the <c>decimal</c> overload but
    /// disambiguates at the call site.
    /// </summary>
    public StrategyBuilder QuantityNotional(decimal dollars) => Quantity(dollars);
    
    // ========================================
    // ENTRY CONDITIONS
    // ========================================
    
    public StrategyBuilder Entry(double price)
    {
        strategy.EntryConditions.Add(new PriceCondition(ConditionType.Entry, price));
        return this;
    }
    
    public StrategyBuilder Breakout(double? level = null)
    {
        strategy.EntryConditions.Add(new PatternCondition(PatternType.Breakout, level));
        return this;
    }
    
    public StrategyBuilder Pullback(double? level = null)
    {
        strategy.EntryConditions.Add(new PatternCondition(PatternType.Pullback, level));
        return this;
    }
    
    public StrategyBuilder IsAboveVwap()
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapAbove));
        return this;
    }
    
    public StrategyBuilder IsBelowVwap()
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapBelow));
        return this;
    }
    
    public StrategyBuilder IsEmaAbove(int period)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.EmaAbove, period));
        return this;
    }
    
    public StrategyBuilder IsEmaBelow(int period)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.EmaBelow, period));
        return this;
    }
    
    public StrategyBuilder IsDiPositive()
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.DiPositive));
        return this;
    }
    
    public StrategyBuilder IsDiNegative()
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.DiNegative));
        return this;
    }
    
    public StrategyBuilder IsAdxAbove(double threshold)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.AdxAbove, threshold));
        return this;
    }
    
    public StrategyBuilder IsRsiOversold(double threshold = 30)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.RsiOversold, threshold));
        return this;
    }
    
    public StrategyBuilder IsRsiOverbought(double threshold = 70)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.RsiOverbought, threshold));
        return this;
    }
    
    public StrategyBuilder IsMacdBullish()
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.MacdBullish));
        return this;
    }
    
    public StrategyBuilder IsMacdBearish()
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.MacdBearish));
        return this;
    }
    
    public StrategyBuilder IsGapUp(double minPercent = 3)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.GapUp, minPercent));
        return this;
    }
    
    public StrategyBuilder IsGapDown(double minPercent = 3)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.GapDown, minPercent));
        return this;
    }
    
    public StrategyBuilder IsVolumeAbove(double multiplier)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.VolumeAbove, multiplier));
        return this;
    }

    /// <summary>
    /// Price must hold above this level (used for support confirmation).
    /// Example: HoldsAbove(0.48) - price must stay above $0.48
    /// </summary>
    public StrategyBuilder HoldsAbove(double price)
    {
        strategy.EntryConditions.Add(new PriceLevelCondition(PriceLevelType.HoldsAbove, price));
        return this;
    }

    /// <summary>
    /// Price must hold below this level (used for resistance confirmation in shorts).
    /// Example: HoldsBelow(150) - price must stay below $150
    /// </summary>
    public StrategyBuilder HoldsBelow(double price)
    {
        strategy.EntryConditions.Add(new PriceLevelCondition(PriceLevelType.HoldsBelow, price));
        return this;
    }

    /// <summary>
    /// Price must be near a specific level (within tolerance %).
    /// Example: IsNear(3.68, 1.0) - price within 1% of $3.68
    /// </summary>
    public StrategyBuilder IsNear(double price, double tolerancePercent = 1.0)
    {
        strategy.EntryConditions.Add(new PriceLevelCondition(PriceLevelType.Near, price, tolerancePercent));
        return this;
    }

    // ========================================
    // EMA FAMILY — generic period
    // ========================================

    /// <summary>Price is above the N-period EMA. Example: IsAboveEma(9).</summary>
    public StrategyBuilder IsAboveEma(int period)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.EmaAbove, period));
        return this;
    }

    /// <summary>Price is below the N-period EMA. Example: IsBelowEma(21).</summary>
    public StrategyBuilder IsBelowEma(int period)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.EmaBelow, period));
        return this;
    }

    /// <summary>
    /// Price sits between the fast and slow EMA — the "pullback zone" in the
    /// classic 9/30 pullback continuation setup. Pair with RequireEmaStack to
    /// confirm the stack direction (otherwise this fires inside downtrends too).
    /// Example: IsBetweenEma(9, 31).
    /// </summary>
    public StrategyBuilder IsBetweenEma(int fastPeriod, int slowPeriod)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.BetweenEma, fastPeriod, slowPeriod));
        return this;
    }

    /// <summary>
    /// Confirms the EMA stack direction: fast EMA above slow EMA = uptrend.
    /// Lives in the FILTERS phase — always-on regime gate, not a trigger.
    /// Example: RequireEmaStack(9, 31).
    /// </summary>
    public StrategyBuilder RequireEmaStack(int fastPeriod, int slowPeriod)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.EmaStack, fastPeriod, slowPeriod, StrategyPhase.Filters));
        return this;
    }

    /// <summary>
    /// Trigger: prior bar closed at-or-below the N-period EMA AND current bar
    /// closed back above it. This is the classic reclaim trigger for pullback
    /// continuation entries. Example: OnReclaim(9).
    /// </summary>
    public StrategyBuilder OnReclaim(int emaPeriod)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.ReclaimEma, emaPeriod));
        return this;
    }

    // ========================================
    // VWAP RECLAIM / LOSS
    // ========================================

    /// <summary>Trigger: prior bar at-or-below VWAP, current bar above VWAP.</summary>
    public StrategyBuilder OnVwapReclaim()
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapReclaim));
        return this;
    }

    /// <summary>Trigger: prior bar at-or-above VWAP, current bar below VWAP.</summary>
    public StrategyBuilder OnVwapLoss()
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapLoss));
        return this;
    }

    // ========================================
    // REGIME FILTERS (Phase = Filters)
    // ========================================

    /// <summary>Filter: ADX must be above threshold (trending market). Default 20.</summary>
    public StrategyBuilder RequireAdxAbove(double threshold = 20)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.AdxAbove, threshold, null, StrategyPhase.Filters));
        return this;
    }

    // ========================================
    // VOLUME CONFIRM
    // ========================================

    /// <summary>
    /// Trigger-bar volume must be at least <paramref name="multiplier"/>× the
    /// rolling average (default 1.2). Common pairing with OnReclaim to filter
    /// low-conviction triggers. Example: WithVolumeConfirm(1.2).
    /// </summary>
    public StrategyBuilder WithVolumeConfirm(double multiplier = 1.2)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.VolumeAbove, multiplier));
        return this;
    }

    // ========================================
    // RSI DIVERGENCE
    // ========================================

    public StrategyBuilder IsRsiBullishDivergence()
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.RsiBullishDivergence));
        return this;
    }

    public StrategyBuilder IsRsiBearishDivergence()
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.RsiBearishDivergence));
        return this;
    }

    // ========================================
    // SUPPORT / RESISTANCE
    // ========================================

    /// <summary>Price within tolerancePercent of the recent swing low. Default tolerance 0.5%.</summary>
    public StrategyBuilder IsAtSupport(double tolerancePercent = 0.5)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.AtSupport, tolerancePercent));
        return this;
    }

    /// <summary>Price within tolerancePercent of the recent swing high. Default tolerance 0.5%.</summary>
    public StrategyBuilder IsAtResistance(double tolerancePercent = 0.5)
    {
        strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.AtResistance, tolerancePercent));
        return this;
    }

    // ========================================
    // CANDLESTICK PATTERNS
    // ========================================

    public StrategyBuilder IsBullishEngulfing()
    {
        strategy.EntryConditions.Add(new PatternCondition(PatternType.BullishEngulfing));
        return this;
    }

    public StrategyBuilder IsBearishEngulfing()
    {
        strategy.EntryConditions.Add(new PatternCondition(PatternType.BearishEngulfing));
        return this;
    }

    public StrategyBuilder IsHammer()
    {
        strategy.EntryConditions.Add(new PatternCondition(PatternType.Hammer));
        return this;
    }

    public StrategyBuilder IsShootingStar()
    {
        strategy.EntryConditions.Add(new PatternCondition(PatternType.ShootingStar));
        return this;
    }

    public StrategyBuilder IsDoji()
    {
        strategy.EntryConditions.Add(new PatternCondition(PatternType.Doji));
        return this;
    }

    // ========================================
    // ALIASES — natural-language phrasings
    // Each forwards to the canonical verb so Claude-generated scripts compile
    // regardless of which phrasing the LLM picked.
    // ========================================

    public StrategyBuilder AboveVwap() => IsAboveVwap();
    public StrategyBuilder BelowVwap() => IsBelowVwap();
    public StrategyBuilder Oversold(double threshold = 30) => IsRsiOversold(threshold);
    public StrategyBuilder Overbought(double threshold = 70) => IsRsiOverbought(threshold);
    public StrategyBuilder BullishMacd() => IsMacdBullish();
    public StrategyBuilder BearishMacd() => IsMacdBearish();
    public StrategyBuilder Trending(double minAdx = 20) => RequireAdxAbove(minAdx);
    public StrategyBuilder GapUp(double minPercent = 3) => IsGapUp(minPercent);
    public StrategyBuilder GapDown(double minPercent = 3) => IsGapDown(minPercent);
    public StrategyBuilder VolumeSpike(double multiplier = 2.0) => IsVolumeAbove(multiplier);

    // ========================================
    // ORDER DIRECTION
    // ========================================
    
    public StrategyBuilder Order(TradeDirection direction = TradeDirection.Long)
    {
        strategy.Direction = direction;
        return this;
    }
    
    public StrategyBuilder Long() => Order(TradeDirection.Long);
    public StrategyBuilder Short() => Order(TradeDirection.Short);
    
    // ========================================
    // EXIT CONDITIONS
    // ========================================
    
    public StrategyBuilder TakeProfit(double price)
    {
        strategy.TakeProfitPrice = price;
        return this;
    }

    /// <summary>
    /// Sets multiple take profit targets for scaling out.
    /// Example: TakeProfit(5.00, 6.50, 8.00) - T1: $5, T2: $6.50, T3: $8
    /// </summary>
    public StrategyBuilder TakeProfit(double t1, double t2, double? t3 = null)
    {
        strategy.TakeProfitTargets.Clear();
        strategy.TakeProfitTargets.Add(new TakeProfitTarget { Price = t1, PercentToSell = t3.HasValue ? 33 : 50, Label = "T1" });
        strategy.TakeProfitTargets.Add(new TakeProfitTarget { Price = t2, PercentToSell = t3.HasValue ? 33 : 50, Label = "T2" });
        if (t3.HasValue)
            strategy.TakeProfitTargets.Add(new TakeProfitTarget { Price = t3.Value, PercentToSell = 34, Label = "T3" });
        strategy.TakeProfitPrice = t1; // Primary target for simple exits
        return this;
    }

    /// <summary>
    /// Adds a specific take profit target with custom percentage to sell.
    /// Example: AddTarget(5.00, 50, "T1") - sell 50% at $5
    /// </summary>
    public StrategyBuilder AddTarget(double price, int percentToSell, string? label = null)
    {
        strategy.TakeProfitTargets.Add(new TakeProfitTarget 
        { 
            Price = price, 
            PercentToSell = percentToSell, 
            Label = label ?? $"T{strategy.TakeProfitTargets.Count + 1}" 
        });
        if (!strategy.TakeProfitPrice.HasValue)
            strategy.TakeProfitPrice = price;
        return this;
    }

    public StrategyBuilder TakeProfitPercent(double percent)
    {
        strategy.TakeProfitPercent = percent;
        return this;
    }
    
    public StrategyBuilder StopLoss(double price)
    {
        strategy.StopLossPrice = price;
        return this;
    }
    
    public StrategyBuilder StopLossPercent(double percent)
    {
        strategy.StopLossPercent = percent;
        return this;
    }
    
    public StrategyBuilder TrailingStopLoss(double percent)
    {
        strategy.TrailingStopPercent = percent;
        return this;
    }
    
    public StrategyBuilder ExitStrategy(TimeSpan timeOfDay)
    {
        strategy.ExitTime = timeOfDay;
        return this;
    }
    
    // ========================================
    // BRANCHING LOGIC
    // ========================================

    /// <summary>
    /// Starts a conditional branch using the last-added condition as the "if".
    /// Usage: .IsAboveVwap().Then(b => b.Long().TakeProfit(5.00))
    ///        .ElseIf(c => c.IsBelowVwap(), b => b.Short().TakeProfit(3.00))
    ///        .Else(b => b.Long().TakeProfit(4.00))
    /// </summary>
    public ConditionalBuilder Then(Action<BranchBuilder> configure)
    {
        if (strategy.EntryConditions.Count == 0)
            throw new InvalidOperationException("Then() requires a preceding condition (e.g. .IsAboveVwap().Then(...))");

        // Pop the last condition to use as the "if" condition
        var condition = strategy.EntryConditions[^1];
        strategy.EntryConditions.RemoveAt(strategy.EntryConditions.Count - 1);

        var block = new ConditionalBlock();
        strategy.ConditionalBlocks.Add(block);

        // Build the "then" branch
        var builder = new BranchBuilder();
        configure(builder);
        block.Branches.Add(new ConditionalBranch { Condition = condition, Overrides = builder.Overrides });

        return new ConditionalBuilder(this, block);
    }

    // ========================================
    // ADVANCED
    // ========================================
    
    public StrategyBuilder AutonomousTrading()
    {
        strategy.IsAutonomous = true;
        return this;
    }
    
    public StrategyBuilder AdaptiveOrder()
    {
        strategy.IsAdaptive = true;
        return this;
    }
    
    public StrategyBuilder Repeat()
    {
        strategy.ShouldRepeat = true;
        return this;
    }
    
    // ========================================
    // BUILD
    // ========================================
    
    public StrategyDefinition Build() => strategy;
    
    /// <summary>
    /// Serializes the strategy to IdiotScript text format.
    /// </summary>
    public string ToScript()
    {
        // Quote the ticker so the output parses back via ScriptParser (its regex expects
        // Ticker("SYM")). The previous unquoted Ticker(SYM) failed to parse on reload.
        var parts = new List<string> { $"Ticker(\"{strategy.Symbol}\")" };
        
        if (!string.IsNullOrEmpty(strategy.Name))
            parts.Add($"Name(\"{strategy.Name}\")");
        
        if (strategy.Session != TradingSession.RTH)
            parts.Add($"Session(IS.{strategy.Session.ToString().ToUpperInvariant()})");
        
        // Notional ("$1000 of TSLA") and share sizing are mutually exclusive. The
        // old code only emitted Quantity(shares); a notional-sized strategy
        // serialized with NO size token and round-tripped to "use workspace default".
        if (strategy.IsNotional)
            parts.Add($"QuantityNotional({strategy.NotionalAmount!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        else if (strategy.Quantity > 0)
            parts.Add($"Quantity({strategy.Quantity})");
        
        // Entry conditions
        foreach (var cond in strategy.EntryConditions)
        {
            parts.Add(cond.ToScript());
        }

        // Conditional blocks
        foreach (var block in strategy.ConditionalBlocks)
        {
            parts.Add(block.ToScript());
        }

        // Direction
        if (strategy.Direction == TradeDirection.Short)
            parts.Add("Short()");
        else
            parts.Add("Long()");

        // Exit conditions. Emit the multi-target form when the strategy scales out so
        // T2/T3 survive a round trip (the single TakeProfitPrice is only T1).
        if (strategy.TakeProfitTargets.Count > 1)
            parts.Add($"TakeProfit({string.Join(", ", strategy.TakeProfitTargets.Take(3).Select(t => Inv(t.Price)))})");
        else if (strategy.TakeProfitPrice.HasValue)
            parts.Add($"TakeProfit({Inv(strategy.TakeProfitPrice.Value)})");
        if (strategy.StopLossPrice.HasValue)
            parts.Add($"StopLoss({Inv(strategy.StopLossPrice.Value)})");
        if (strategy.TrailingStopPercent.HasValue)
            parts.Add($"TrailingStopLoss({strategy.TrailingStopPercent})");
        
        // Advanced
        if (strategy.IsAutonomous)
            parts.Add("AutonomousTrading()");
        if (strategy.IsAdaptive)
            parts.Add("AdaptiveOrder()");
        if (strategy.ShouldRepeat)
            parts.Add("Repeat()");
        
        return string.Join("\n    .", parts);
    }

    // Invariant number formatting so a comma-decimal machine locale can't emit "5,00"
    // (the parser parses numeric args with InvariantCulture).
    private static string Inv(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Strategy definition built from IdiotScript.
/// </summary>
public sealed class StrategyDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Symbol { get; set; } = "";
    public string? Name { get; set; }
    public TradingSession Session { get; set; } = TradingSession.RTH;

    /// <summary>
    /// Position size as a count of shares. <c>0</c> means "use the workspace's
    /// default size at fire time." Mutually exclusive with
    /// <see cref="NotionalAmount"/> — set via <c>Quantity(int)</c> for shares
    /// or <c>Quantity(decimal)</c> for notional dollars.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Position size as a dollar amount (Alpaca <c>notional</c>). Null when
    /// the strategy sizes by shares. The broker layer routes whichever field
    /// is set. Notional sizing is the cleanest way to express
    /// "5% of portfolio per trade" without recomputing share counts.
    /// </summary>
    public decimal? NotionalAmount { get; set; }

    /// <summary>True when the strategy sizes by dollars rather than shares.</summary>
    public bool IsNotional => NotionalAmount.HasValue;
    
    public List<ICondition> EntryConditions { get; } = [];
    public TradeDirection Direction { get; set; } = TradeDirection.Long;
    
    public double? TakeProfitPrice { get; set; }
    public double? TakeProfitPercent { get; set; }
    public List<TakeProfitTarget> TakeProfitTargets { get; } = [];
    public double? StopLossPrice { get; set; }
    public double? StopLossPercent { get; set; }
    public double? TrailingStopPercent { get; set; }
    public TimeSpan? ExitTime { get; set; }

    public bool IsAutonomous { get; set; }
    public bool IsAdaptive { get; set; }
    public bool ShouldRepeat { get; set; }

    /// <summary>
    /// Checks if this strategy has multiple take profit targets.
    /// </summary>
    public bool HasMultipleTargets => TakeProfitTargets.Count > 1;

    public List<ConditionalBlock> ConditionalBlocks { get; } = [];
    public bool HasBranching => ConditionalBlocks.Count > 0;
}

/// <summary>
/// Represents a single take profit target for scaling out.
/// </summary>
 public sealed class TakeProfitTarget
{
    public string Label { get; set; } = "T1";
    public double Price { get; set; }
    public int PercentToSell { get; set; } = 100;
    public bool IsHit { get; set; }
    public DateTime? HitTime { get; set; }

    public override string ToString() => $"{Label}: ${Price:F2} ({PercentToSell}%)";
}

// ========================================
// CONDITION TYPES
// ========================================

/// <summary>
/// The lifecycle phase a condition or action belongs to. The visual builder
/// renders one card per phase; the parser rejects verbs used outside their phase.
/// Order matches the DSL spec in CLAUDE.md.
/// </summary>
public enum StrategyPhase
{
    Setup,    // ticker, session, account, window
    Filters,  // regime preconditions (always-on gates)
    Entry,    // trigger conditions ("the fire")
    Order,    // direction, quantity, type, price
    Risk,     // stop, trailing
    Exit      // targets, time exits, condition exits
}

public interface ICondition
{
    string ToScript();
    bool Evaluate(IndicatorSnapshot indicators);

    /// <summary>
    /// The phase this condition belongs to. Default Entry; filter conditions
    /// override to Filters; exit conditions override to Exit.
    /// </summary>
    StrategyPhase Phase => StrategyPhase.Entry;
}

/// <summary>
/// Condition algebra — boolean composition operators that let the DSL build
/// expressions like <c>IsAboveVwap.And(IsEmaAbove(9))</c>. Returns wrapping
/// conditions that delegate to their operands at evaluation time.
/// </summary>
public static class ConditionExtensions
{
    public static ICondition And(this ICondition a, ICondition b) => new AndCondition(a, b);
    public static ICondition Or(this ICondition a, ICondition b) => new OrCondition(a, b);
    public static ICondition Not(this ICondition a) => new NotCondition(a);
}

public sealed class AndCondition(ICondition left, ICondition right) : ICondition
{
    public ICondition Left { get; } = left;
    public ICondition Right { get; } = right;
    public string ToScript() => $"{Left.ToScript()}.And({Right.ToScript()})";
    public bool Evaluate(IndicatorSnapshot s) => Left.Evaluate(s) && Right.Evaluate(s);
    public StrategyPhase Phase => Left.Phase; // left wins; mixed-phase composition is parser-rejected
}

public sealed class OrCondition(ICondition left, ICondition right) : ICondition
{
    public ICondition Left { get; } = left;
    public ICondition Right { get; } = right;
    public string ToScript() => $"{Left.ToScript()}.Or({Right.ToScript()})";
    public bool Evaluate(IndicatorSnapshot s) => Left.Evaluate(s) || Right.Evaluate(s);
    public StrategyPhase Phase => Left.Phase;
}

public sealed class NotCondition(ICondition inner) : ICondition
{
    public ICondition Inner { get; } = inner;
    public string ToScript() => $"{Inner.ToScript()}.Not()";
    public bool Evaluate(IndicatorSnapshot s) => !Inner.Evaluate(s);
    public StrategyPhase Phase => Inner.Phase;
}

public enum ConditionType { Entry, Breakout, Pullback }

public enum PatternType
{
    Breakout, Pullback,
    BullishEngulfing, BearishEngulfing,
    Hammer, ShootingStar, Doji
}

public enum IndicatorType
{
    VwapAbove, VwapBelow, VwapReclaim, VwapLoss,
    EmaAbove, EmaBelow, BetweenEma, EmaStack, ReclaimEma,
    DiPositive, DiNegative,
    AdxAbove,
    RsiOversold, RsiOverbought, RsiBullishDivergence, RsiBearishDivergence,
    MacdBullish, MacdBearish,
    GapUp, GapDown,
    VolumeAbove,
    AtSupport, AtResistance
}

/// <summary>
/// Types of price level conditions.
/// </summary>
 public enum PriceLevelType
{
    HoldsAbove,   // Price must stay above this level
    HoldsBelow,   // Price must stay below this level
    Near,         // Price must be near this level (within tolerance)
    BreaksAbove,  // Price must break above this level
    BreaksBelow   // Price must break below this level
}

public sealed class PriceCondition(ConditionType type, double price) : ICondition
{
    public ConditionType Type { get; } = type;
    public double Price { get; } = price;
    
    public string ToScript() => $"Entry({Price})";
    public bool Evaluate(IndicatorSnapshot indicators) => Type switch
    {
        ConditionType.Entry => indicators.Price >= Price,
        _ => true
    };
}

public sealed class PatternCondition(PatternType type, double? level = null) : ICondition
{
    public PatternType Type { get; } = type;
    public double? Level { get; } = level;

    public string ToScript() => Level.HasValue ? $"{Type}({Level})" : $"{Type}()";

    public bool Evaluate(IndicatorSnapshot s) => Type switch
    {
        PatternType.BullishEngulfing => s.IsBullishEngulfing,
        PatternType.BearishEngulfing => s.IsBearishEngulfing,
        PatternType.Hammer           => s.IsHammer,
        PatternType.ShootingStar     => s.IsShootingStar,
        PatternType.Doji             => s.IsDoji,
        _                            => true // Breakout / Pullback evaluated by pattern engine elsewhere
    };
}

public sealed class IndicatorCondition(IndicatorType type, double? parameter = null, double? parameter2 = null, StrategyPhase phase = StrategyPhase.Entry) : ICondition
{
    public IndicatorType Type { get; } = type;

    /// <summary>Primary parameter — period for EMA verbs, threshold for ADX/RSI, multiplier for Volume.</summary>
    public double? Parameter { get; } = parameter;

    /// <summary>Secondary parameter — slow EMA period for IsBetweenEma(fast, slow) and EmaStack(fast, slow).</summary>
    public double? Parameter2 { get; } = parameter2;

    public StrategyPhase Phase { get; } = phase;

    public string ToScript()
    {
        // Emit the canonical verb the parser (ScriptParser.ApplyVerb) recognizes.
        // The naive $"Is{Type}" produced tokens like "IsVwapAbove" / "IsEmaStack" /
        // "IsReclaimEma" that have no parser case, so those conditions were silently
        // dropped on a serialize→parse round trip (a strategy lost its VWAP/EMA-stack
        // gate). Map each IndicatorType to its builder verb explicitly.
        var verb = Type switch
        {
            IndicatorType.VwapAbove            => "IsAboveVwap",
            IndicatorType.VwapBelow            => "IsBelowVwap",
            IndicatorType.VwapReclaim          => "OnVwapReclaim",
            IndicatorType.VwapLoss             => "OnVwapLoss",
            IndicatorType.EmaAbove             => "IsAboveEma",
            IndicatorType.EmaBelow             => "IsBelowEma",
            IndicatorType.BetweenEma           => "IsBetweenEma",
            IndicatorType.EmaStack             => "RequireEmaStack",
            IndicatorType.ReclaimEma           => "OnReclaim",
            IndicatorType.DiPositive           => "IsDiPositive",
            IndicatorType.DiNegative           => "IsDiNegative",
            IndicatorType.AdxAbove             => "IsAdxAbove",
            IndicatorType.RsiOversold          => "IsRsiOversold",
            IndicatorType.RsiOverbought        => "IsRsiOverbought",
            IndicatorType.RsiBullishDivergence => "IsRsiBullishDivergence",
            IndicatorType.RsiBearishDivergence => "IsRsiBearishDivergence",
            IndicatorType.MacdBullish          => "IsMacdBullish",
            IndicatorType.MacdBearish          => "IsMacdBearish",
            IndicatorType.GapUp                => "IsGapUp",
            IndicatorType.GapDown              => "IsGapDown",
            IndicatorType.VolumeAbove          => "IsVolumeAbove",
            IndicatorType.AtSupport            => "IsAtSupport",
            IndicatorType.AtResistance         => "IsAtResistance",
            _                                  => $"Is{Type}"
        };

        return (Parameter.HasValue, Parameter2.HasValue) switch
        {
            (true, true)  => $"{verb}({Fmt(Parameter.Value)}, {Fmt(Parameter2.Value)})",
            (true, false) => $"{verb}({Fmt(Parameter.Value)})",
            _             => $"{verb}()"
        };
    }

    // Invariant formatting so a comma-decimal machine locale can't emit "1,5"
    // (the parser parses args with InvariantCulture).
    private static string Fmt(double v) =>
        v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool Evaluate(IndicatorSnapshot s) => Type switch
    {
        IndicatorType.VwapAbove          => s.VwapDistance > 0,
        IndicatorType.VwapBelow          => s.VwapDistance < 0,
        IndicatorType.VwapReclaim        => s.PriorPrice is { } pp && s.PriorVwap is { } pv
                                              && pp <= pv && s.Price > (s.Vwap ?? 0),
        IndicatorType.VwapLoss           => s.PriorPrice is { } pp2 && s.PriorVwap is { } pv2
                                              && pp2 >= pv2 && s.Price < (s.Vwap ?? 0),
        IndicatorType.EmaAbove           => Parameter is { } p && s.GetEma((int)p) is { } e && s.Price > e,
        IndicatorType.EmaBelow           => Parameter is { } p2 && s.GetEma((int)p2) is { } e2 && s.Price < e2,
        IndicatorType.BetweenEma         => Parameter is { } fast && Parameter2 is { } slow
                                              && s.GetEma((int)fast) is { } emaFast
                                              && s.GetEma((int)slow) is { } emaSlow
                                              && Math.Min(emaFast, emaSlow) <= s.Price
                                              && s.Price <= Math.Max(emaFast, emaSlow),
        IndicatorType.EmaStack           => Parameter is { } sf && Parameter2 is { } ss
                                              && s.GetEma((int)sf) is { } stackFast
                                              && s.GetEma((int)ss) is { } stackSlow
                                              && stackFast > stackSlow,
        IndicatorType.ReclaimEma         => Parameter is { } rp
                                              && s.GetPriorEma((int)rp) is { } priorEma
                                              && s.GetEma((int)rp) is { } currentEma
                                              && s.PriorPrice is { } priorPrice
                                              && priorPrice <= priorEma && s.Price > currentEma,
        IndicatorType.DiPositive         => s.IsBullishTrend,
        IndicatorType.DiNegative         => !s.IsBullishTrend,
        IndicatorType.AdxAbove           => s.Adx >= (Parameter ?? 20),
        IndicatorType.RsiOversold        => s.Rsi <= (Parameter ?? 30),
        IndicatorType.RsiOverbought      => s.Rsi >= (Parameter ?? 70),
        IndicatorType.RsiBullishDivergence => s.HasBullishDivergence == true,
        IndicatorType.RsiBearishDivergence => s.HasBearishDivergence == true,
        IndicatorType.MacdBullish        => s.IsMacdBullish,
        IndicatorType.MacdBearish        => !s.IsMacdBullish,
        IndicatorType.VolumeAbove        => s.VolumeRatio >= (Parameter ?? 1.5),
        IndicatorType.AtSupport          => s.RecentSwingLow is { } sl
                                              && Math.Abs((s.Price - sl) / sl) * 100.0 <= (Parameter ?? 0.5),
        IndicatorType.AtResistance       => s.RecentSwingHigh is { } sh
                                              && Math.Abs((s.Price - sh) / sh) * 100.0 <= (Parameter ?? 0.5),
        _                                => true
    };
}

/// <summary>
/// Condition based on price level (support/resistance).
/// Used for HoldsAbove(), HoldsBelow(), IsNear(), etc.
/// </summary>
public sealed class PriceLevelCondition : ICondition
{
    public PriceLevelType Type { get; }
    public double Level { get; }
    public double TolerancePercent { get; }

    // Track if price has violated the level (for HoldsAbove/HoldsBelow)
    private double lowestSeen = double.MaxValue;
    private double highestSeen = double.MinValue;
    private double? previousPrice;

    // Auto-reset when the evaluation context changes (different symbol or new session date).
    private string? lastSymbol;
    private DateOnly? lastSessionDate;

    public PriceLevelCondition(PriceLevelType type, double level, double tolerancePercent = 1.0)
    {
        Type = type;
        Level = level;
        TolerancePercent = tolerancePercent;
    }

    public string ToScript() => Type switch
    {
        PriceLevelType.HoldsAbove => $"HoldsAbove({Level})",
        PriceLevelType.HoldsBelow => $"HoldsBelow({Level})",
        PriceLevelType.Near => $"IsNear({Level}, {TolerancePercent})",
        PriceLevelType.BreaksAbove => $"BreaksAbove({Level})",
        PriceLevelType.BreaksBelow => $"BreaksBelow({Level})",
        _ => $"PriceLevel({Level})"
    };

    /// <summary>
    /// Updates tracking and evaluates the condition.
    /// </summary>
    public bool Evaluate(IndicatorSnapshot indicators)
    {
        var price = indicators.Price;
        var sessionDate = DateOnly.FromDateTime(indicators.Timestamp);
        var symbol = indicators.Symbol;

        // Auto-reset on context change so accumulated extremes don't leak across symbols/sessions.
        if (lastSymbol != null && (lastSymbol != symbol || lastSessionDate != sessionDate))
            Reset();

        lastSymbol = symbol;
        lastSessionDate = sessionDate;

        // Capture previous-bar side before updating extremes/previousPrice.
        var prior = previousPrice;

        // Track extremes
        if (price < lowestSeen) lowestSeen = price;
        if (price > highestSeen) highestSeen = price;

        var result = Type switch
        {
            // HoldsAbove: True if price is currently above AND has never gone significantly below
            PriceLevelType.HoldsAbove => price >= Level && lowestSeen >= Level * 0.995, // 0.5% tolerance

            // HoldsBelow: True if price is currently below AND has never gone significantly above
            PriceLevelType.HoldsBelow => price <= Level && highestSeen <= Level * 1.005,

            // Near: True if price is within tolerance % of level
            PriceLevelType.Near => Math.Abs((price - Level) / Level * 100) <= TolerancePercent,

            // BreaksAbove: only fires on the cross (prior bar at-or-below, current bar above).
            PriceLevelType.BreaksAbove => prior is { } p1 && p1 <= Level && price > Level,

            // BreaksBelow: only fires on the cross (prior bar at-or-above, current bar below).
            PriceLevelType.BreaksBelow => prior is { } p2 && p2 >= Level && price < Level,

            _ => true
        };

        previousPrice = price;
        return result;
    }

    /// <summary>
    /// Resets tracking state. Called automatically on symbol/session change; can be called manually too.
    /// </summary>
    public void Reset()
    {
        lowestSeen = double.MaxValue;
        highestSeen = double.MinValue;
        previousPrice = null;
    }
}

// ========================================
// BRANCHING LOGIC
// ========================================

/// <summary>
/// A conditional block containing If/ElseIf/Else branches.
/// At evaluation time, the first matching branch's overrides are applied.
/// </summary>
public sealed class ConditionalBlock
{
    public List<ConditionalBranch> Branches { get; } = [];

    /// <summary>
    /// Evaluates branches in order and returns the first match.
    /// Returns null if no branch matches (no Else and no conditions met).
    /// </summary>
    public ConditionalBranch? Evaluate(IndicatorSnapshot snapshot)
    {
        foreach (var branch in Branches)
        {
            if (branch.Condition is null || branch.Condition.Evaluate(snapshot))
                return branch;
        }
        return null;
    }

    public string ToScript()
    {
        var parts = new List<string>();
        for (int i = 0; i < Branches.Count; i++)
        {
            var branch = Branches[i];
            if (i == 0)
            {
                parts.Add($"{branch.Condition!.ToScript()}");
                parts.Add($"    .Then({branch.Overrides.ToScript()})");
            }
            else if (branch.Condition is not null)
            {
                parts.Add($"    .ElseIf({branch.Condition.ToScript()}, {branch.Overrides.ToScript()})");
            }
            else
            {
                parts.Add($"    .Else({branch.Overrides.ToScript()})");
            }
        }
        return string.Join("\n", parts);
    }
}

/// <summary>
/// A single branch in a conditional block.
/// </summary>
public sealed class ConditionalBranch
{
    /// <summary>
    /// The condition to evaluate. Null means this is the Else (default) branch.
    /// </summary>
    public ICondition? Condition { get; set; }

    /// <summary>
    /// Strategy overrides to apply when this branch matches.
    /// </summary>
    public StrategyOverrides Overrides { get; set; } = new();
}

/// <summary>
/// Partial strategy settings that override the base strategy when a branch matches.
/// Only non-null properties are applied.
/// </summary>
public sealed class StrategyOverrides
{
    public TradeDirection? Direction { get; set; }
    public List<ICondition> EntryConditions { get; } = [];
    public double? TakeProfitPrice { get; set; }
    public List<TakeProfitTarget> TakeProfitTargets { get; } = [];
    public double? StopLossPrice { get; set; }
    public double? StopLossPercent { get; set; }
    public double? TrailingStopPercent { get; set; }

    public string ToScript()
    {
        var parts = new List<string>();
        if (Direction.HasValue)
            parts.Add(Direction == TradeDirection.Short ? "Short()" : "Long()");
        foreach (var cond in EntryConditions)
            parts.Add(cond.ToScript());
        if (TakeProfitTargets.Count > 0)
            parts.Add($"TakeProfit({string.Join(", ", TakeProfitTargets.Select(t => t.Price.ToString()))})");
        else if (TakeProfitPrice.HasValue)
            parts.Add($"TakeProfit({TakeProfitPrice})");
        if (StopLossPrice.HasValue)
            parts.Add($"StopLoss({StopLossPrice})");
        if (StopLossPercent.HasValue)
            parts.Add($"StopLossPercent({StopLossPercent})");
        if (TrailingStopPercent.HasValue)
            parts.Add($"TrailingStopLoss({TrailingStopPercent})");
        return string.Join(".", parts);
    }

    /// <summary>
    /// Applies these overrides to a strategy definition.
    /// Only non-null properties are applied; everything else keeps the base value.
    /// </summary>
    public void ApplyTo(StrategyDefinition strategy)
    {
        if (Direction.HasValue)
            strategy.Direction = Direction.Value;
        if (EntryConditions.Count > 0)
            foreach (var c in EntryConditions)
                strategy.EntryConditions.Add(c);
        if (TakeProfitPrice.HasValue)
            strategy.TakeProfitPrice = TakeProfitPrice;
        if (TakeProfitTargets.Count > 0)
        {
            strategy.TakeProfitTargets.Clear();
            foreach (var t in TakeProfitTargets)
                strategy.TakeProfitTargets.Add(t);
        }
        if (StopLossPrice.HasValue)
            strategy.StopLossPrice = StopLossPrice;
        if (StopLossPercent.HasValue)
            strategy.StopLossPercent = StopLossPercent;
        if (TrailingStopPercent.HasValue)
            strategy.TrailingStopPercent = TrailingStopPercent;
    }
}

/// <summary>
/// Fluent builder for configuring a branch's strategy overrides.
/// Used inside Then(), ElseIf(), and Else() lambdas.
/// </summary>
public sealed class BranchBuilder
{
    internal StrategyOverrides Overrides { get; } = new();

    public BranchBuilder Long()
    {
        Overrides.Direction = TradeDirection.Long;
        return this;
    }

    public BranchBuilder Short()
    {
        Overrides.Direction = TradeDirection.Short;
        return this;
    }

    public BranchBuilder TakeProfit(double price)
    {
        Overrides.TakeProfitPrice = price;
        return this;
    }

    public BranchBuilder TakeProfit(double t1, double t2, double? t3 = null)
    {
        Overrides.TakeProfitTargets.Clear();
        Overrides.TakeProfitTargets.Add(new TakeProfitTarget { Price = t1, PercentToSell = t3.HasValue ? 33 : 50, Label = "T1" });
        Overrides.TakeProfitTargets.Add(new TakeProfitTarget { Price = t2, PercentToSell = t3.HasValue ? 33 : 50, Label = "T2" });
        if (t3.HasValue)
            Overrides.TakeProfitTargets.Add(new TakeProfitTarget { Price = t3.Value, PercentToSell = 34, Label = "T3" });
        Overrides.TakeProfitPrice = t1;
        return this;
    }

    public BranchBuilder StopLoss(double price)
    {
        Overrides.StopLossPrice = price;
        return this;
    }

    public BranchBuilder StopLossPercent(double percent)
    {
        Overrides.StopLossPercent = percent;
        return this;
    }

    public BranchBuilder TrailingStopLoss(double percent)
    {
        Overrides.TrailingStopPercent = percent;
        return this;
    }

    public BranchBuilder IsAboveVwap()
    {
        Overrides.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapAbove));
        return this;
    }

    public BranchBuilder IsBelowVwap()
    {
        Overrides.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapBelow));
        return this;
    }

    public BranchBuilder HoldsAbove(double price)
    {
        Overrides.EntryConditions.Add(new PriceLevelCondition(PriceLevelType.HoldsAbove, price));
        return this;
    }

    public BranchBuilder HoldsBelow(double price)
    {
        Overrides.EntryConditions.Add(new PriceLevelCondition(PriceLevelType.HoldsBelow, price));
        return this;
    }
}

/// <summary>
/// Factory for creating conditions inside ElseIf lambdas.
/// Mirrors the condition methods from StrategyBuilder.
/// </summary>
public sealed class ConditionFactory
{
    public ICondition IsAboveVwap() => new IndicatorCondition(IndicatorType.VwapAbove);
    public ICondition IsBelowVwap() => new IndicatorCondition(IndicatorType.VwapBelow);
    public ICondition OnVwapReclaim() => new IndicatorCondition(IndicatorType.VwapReclaim);
    public ICondition OnVwapLoss() => new IndicatorCondition(IndicatorType.VwapLoss);
    public ICondition IsEmaAbove(int period) => new IndicatorCondition(IndicatorType.EmaAbove, period);
    public ICondition IsEmaBelow(int period) => new IndicatorCondition(IndicatorType.EmaBelow, period);
    public ICondition IsAboveEma(int period) => new IndicatorCondition(IndicatorType.EmaAbove, period);
    public ICondition IsBelowEma(int period) => new IndicatorCondition(IndicatorType.EmaBelow, period);
    public ICondition IsBetweenEma(int fast, int slow) => new IndicatorCondition(IndicatorType.BetweenEma, fast, slow);
    public ICondition RequireEmaStack(int fast, int slow) => new IndicatorCondition(IndicatorType.EmaStack, fast, slow, StrategyPhase.Filters);
    public ICondition OnReclaim(int period) => new IndicatorCondition(IndicatorType.ReclaimEma, period);
    public ICondition IsDiPositive() => new IndicatorCondition(IndicatorType.DiPositive);
    public ICondition IsDiNegative() => new IndicatorCondition(IndicatorType.DiNegative);
    public ICondition IsAdxAbove(double threshold) => new IndicatorCondition(IndicatorType.AdxAbove, threshold);
    public ICondition RequireAdxAbove(double threshold = 20) => new IndicatorCondition(IndicatorType.AdxAbove, threshold, null, StrategyPhase.Filters);
    public ICondition IsRsiOversold(double threshold = 30) => new IndicatorCondition(IndicatorType.RsiOversold, threshold);
    public ICondition IsRsiOverbought(double threshold = 70) => new IndicatorCondition(IndicatorType.RsiOverbought, threshold);
    public ICondition IsRsiBullishDivergence() => new IndicatorCondition(IndicatorType.RsiBullishDivergence);
    public ICondition IsRsiBearishDivergence() => new IndicatorCondition(IndicatorType.RsiBearishDivergence);
    public ICondition IsMacdBullish() => new IndicatorCondition(IndicatorType.MacdBullish);
    public ICondition IsMacdBearish() => new IndicatorCondition(IndicatorType.MacdBearish);
    public ICondition IsGapUp(double minPercent = 3) => new IndicatorCondition(IndicatorType.GapUp, minPercent);
    public ICondition IsGapDown(double minPercent = 3) => new IndicatorCondition(IndicatorType.GapDown, minPercent);
    public ICondition IsVolumeAbove(double multiplier) => new IndicatorCondition(IndicatorType.VolumeAbove, multiplier);
    public ICondition WithVolumeConfirm(double multiplier = 1.2) => new IndicatorCondition(IndicatorType.VolumeAbove, multiplier);
    public ICondition IsAtSupport(double tolerancePercent = 0.5) => new IndicatorCondition(IndicatorType.AtSupport, tolerancePercent);
    public ICondition IsAtResistance(double tolerancePercent = 0.5) => new IndicatorCondition(IndicatorType.AtResistance, tolerancePercent);
    public ICondition IsBullishEngulfing() => new PatternCondition(PatternType.BullishEngulfing);
    public ICondition IsBearishEngulfing() => new PatternCondition(PatternType.BearishEngulfing);
    public ICondition IsHammer() => new PatternCondition(PatternType.Hammer);
    public ICondition IsShootingStar() => new PatternCondition(PatternType.ShootingStar);
    public ICondition IsDoji() => new PatternCondition(PatternType.Doji);
    public ICondition HoldsAbove(double price) => new PriceLevelCondition(PriceLevelType.HoldsAbove, price);
    public ICondition HoldsBelow(double price) => new PriceLevelCondition(PriceLevelType.HoldsBelow, price);
    public ICondition IsNear(double price, double tolerance = 1.0) => new PriceLevelCondition(PriceLevelType.Near, price, tolerance);
    public ICondition BreaksAbove(double price) => new PriceLevelCondition(PriceLevelType.BreaksAbove, price);
    public ICondition BreaksBelow(double price) => new PriceLevelCondition(PriceLevelType.BreaksBelow, price);
    public ICondition Breakout(double? level = null) => new PatternCondition(PatternType.Breakout, level);
    public ICondition Pullback(double? level = null) => new PatternCondition(PatternType.Pullback, level);
}

/// <summary>
/// Static condition catalog for the expression-based branching syntax (Option A
/// in CLAUDE.md). Lets users compose:
/// <code>
/// using static IdiotProof.Scripting.Conditions;
/// // ...
/// .If(IsAboveVwap.And(IsEmaAbove(9)).And(OnReclaim(9)))
///     .Then(...)
/// </code>
/// Property-shaped values (no parens) for parameterless conditions; method-shaped
/// for parameterized ones. Combine with <c>.And()</c>, <c>.Or()</c>, <c>.Not()</c>
/// from <see cref="ConditionExtensions"/>.
/// </summary>
public static class Conditions
{
    // ── VWAP ──
    public static ICondition IsAboveVwap        => new IndicatorCondition(IndicatorType.VwapAbove);
    public static ICondition IsBelowVwap        => new IndicatorCondition(IndicatorType.VwapBelow);
    public static ICondition AboveVwap          => IsAboveVwap;
    public static ICondition BelowVwap          => IsBelowVwap;
    public static ICondition OnVwapReclaim      => new IndicatorCondition(IndicatorType.VwapReclaim);
    public static ICondition OnVwapLoss         => new IndicatorCondition(IndicatorType.VwapLoss);

    // ── EMA family ──
    public static ICondition IsAboveEma(int period)                        => new IndicatorCondition(IndicatorType.EmaAbove, period);
    public static ICondition IsBelowEma(int period)                        => new IndicatorCondition(IndicatorType.EmaBelow, period);
    public static ICondition IsEmaAbove(int period)                        => IsAboveEma(period);
    public static ICondition IsEmaBelow(int period)                        => IsBelowEma(period);
    public static ICondition IsBetweenEma(int fast, int slow)              => new IndicatorCondition(IndicatorType.BetweenEma, fast, slow);
    public static ICondition RequireEmaStack(int fast, int slow)           => new IndicatorCondition(IndicatorType.EmaStack, fast, slow, StrategyPhase.Filters);
    public static ICondition OnReclaim(int emaPeriod)                      => new IndicatorCondition(IndicatorType.ReclaimEma, emaPeriod);

    // ── ADX / DI ──
    public static ICondition IsDiPositive       => new IndicatorCondition(IndicatorType.DiPositive);
    public static ICondition IsDiNegative       => new IndicatorCondition(IndicatorType.DiNegative);
    public static ICondition IsAdxAbove(double threshold)                  => new IndicatorCondition(IndicatorType.AdxAbove, threshold);
    public static ICondition RequireAdxAbove(double threshold = 20)        => new IndicatorCondition(IndicatorType.AdxAbove, threshold, null, StrategyPhase.Filters);
    public static ICondition Trending(double minAdx = 20)                  => RequireAdxAbove(minAdx);

    // ── RSI ──
    public static ICondition IsRsiOversold(double threshold = 30)          => new IndicatorCondition(IndicatorType.RsiOversold, threshold);
    public static ICondition IsRsiOverbought(double threshold = 70)        => new IndicatorCondition(IndicatorType.RsiOverbought, threshold);
    public static ICondition Oversold(double threshold = 30)               => IsRsiOversold(threshold);
    public static ICondition Overbought(double threshold = 70)             => IsRsiOverbought(threshold);
    public static ICondition IsRsiBullishDivergence => new IndicatorCondition(IndicatorType.RsiBullishDivergence);
    public static ICondition IsRsiBearishDivergence => new IndicatorCondition(IndicatorType.RsiBearishDivergence);

    // ── MACD ──
    public static ICondition IsMacdBullish      => new IndicatorCondition(IndicatorType.MacdBullish);
    public static ICondition IsMacdBearish      => new IndicatorCondition(IndicatorType.MacdBearish);
    public static ICondition BullishMacd        => IsMacdBullish;
    public static ICondition BearishMacd        => IsMacdBearish;

    // ── Volume ──
    public static ICondition IsVolumeAbove(double multiplier)              => new IndicatorCondition(IndicatorType.VolumeAbove, multiplier);
    public static ICondition WithVolumeConfirm(double multiplier = 1.2)    => new IndicatorCondition(IndicatorType.VolumeAbove, multiplier);
    public static ICondition VolumeSpike(double multiplier = 2.0)          => new IndicatorCondition(IndicatorType.VolumeAbove, multiplier);

    // ── Gap ──
    public static ICondition IsGapUp(double minPercent = 3)                => new IndicatorCondition(IndicatorType.GapUp, minPercent);
    public static ICondition IsGapDown(double minPercent = 3)              => new IndicatorCondition(IndicatorType.GapDown, minPercent);

    // ── Support / Resistance ──
    public static ICondition IsAtSupport(double tolerancePercent = 0.5)    => new IndicatorCondition(IndicatorType.AtSupport, tolerancePercent);
    public static ICondition IsAtResistance(double tolerancePercent = 0.5) => new IndicatorCondition(IndicatorType.AtResistance, tolerancePercent);

    // ── Candlestick patterns ──
    public static ICondition IsBullishEngulfing => new PatternCondition(PatternType.BullishEngulfing);
    public static ICondition IsBearishEngulfing => new PatternCondition(PatternType.BearishEngulfing);
    public static ICondition IsHammer           => new PatternCondition(PatternType.Hammer);
    public static ICondition IsShootingStar     => new PatternCondition(PatternType.ShootingStar);
    public static ICondition IsDoji             => new PatternCondition(PatternType.Doji);

    // ── Price levels ──
    public static ICondition HoldsAbove(double price)                      => new PriceLevelCondition(PriceLevelType.HoldsAbove, price);
    public static ICondition HoldsBelow(double price)                      => new PriceLevelCondition(PriceLevelType.HoldsBelow, price);
    public static ICondition IsNear(double price, double tolerance = 1.0)  => new PriceLevelCondition(PriceLevelType.Near, price, tolerance);
    public static ICondition BreaksAbove(double price)                     => new PriceLevelCondition(PriceLevelType.BreaksAbove, price);
    public static ICondition BreaksBelow(double price)                     => new PriceLevelCondition(PriceLevelType.BreaksBelow, price);
}

/// <summary>
/// Fluent builder for chaining ElseIf/Else after a Then().
/// Returns to StrategyBuilder when the conditional block is complete.
/// </summary>
public sealed class ConditionalBuilder
{
    private readonly StrategyBuilder parent;
    private readonly ConditionalBlock block;

    internal ConditionalBuilder(StrategyBuilder parent, ConditionalBlock block)
    {
        this.parent = parent;
        this.block = block;
    }

    /// <summary>
    /// Adds an ElseIf branch with a condition and actions.
    /// Usage: .ElseIf(c => c.IsBelowVwap(), b => b.Short().TakeProfit(3.00))
    /// </summary>
    public ConditionalBuilder ElseIf(Func<ConditionFactory, ICondition> condition, Action<BranchBuilder> configure)
    {
        var cond = condition(new ConditionFactory());
        var builder = new BranchBuilder();
        configure(builder);
        block.Branches.Add(new ConditionalBranch { Condition = cond, Overrides = builder.Overrides });
        return this;
    }

    /// <summary>
    /// Adds the default Else branch (no condition).
    /// Returns to StrategyBuilder for continued chaining.
    /// Usage: .Else(b => b.Long().TakeProfit(4.00))
    /// </summary>
    public StrategyBuilder Else(Action<BranchBuilder> configure)
    {
        var builder = new BranchBuilder();
        configure(builder);
        block.Branches.Add(new ConditionalBranch { Condition = null, Overrides = builder.Overrides });
        return parent;
    }

    /// <summary>
    /// Ends the conditional block without an Else branch.
    /// Returns to StrategyBuilder for continued chaining.
    /// </summary>
    public StrategyBuilder EndIf() => parent;

    // Delegate common terminal methods to allow chaining without EndIf()
    public StrategyBuilder StopLoss(double price) => parent.StopLoss(price);
    public StrategyBuilder StopLossPercent(double percent) => parent.StopLossPercent(percent);
    public StrategyBuilder TrailingStopLoss(double percent) => parent.TrailingStopLoss(percent);
    public StrategyBuilder Repeat() => parent.Repeat();
    public StrategyBuilder AutonomousTrading() => parent.AutonomousTrading();
    public StrategyBuilder AdaptiveOrder() => parent.AdaptiveOrder();
    public StrategyDefinition Build() => parent.Build();
    public string ToScript() => parent.ToScript();
}
