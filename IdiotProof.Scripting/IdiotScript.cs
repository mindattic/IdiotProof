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
    private readonly StrategyDefinition _strategy = new();
    
    internal StrategyBuilder(string symbol)
    {
        _strategy.Symbol = symbol.ToUpperInvariant();
    }
    
    // ========================================
    // CONFIGURATION
    // ========================================
    
    public StrategyBuilder Name(string name)
    {
        _strategy.Name = name;
        return this;
    }
    
    public StrategyBuilder Session(TradingSession session)
    {
        _strategy.Session = session;
        return this;
    }
    
    public StrategyBuilder Quantity(int quantity)
    {
        _strategy.Quantity = quantity;
        return this;
    }
    
    // ========================================
    // ENTRY CONDITIONS
    // ========================================
    
    public StrategyBuilder Entry(double price)
    {
        _strategy.EntryConditions.Add(new PriceCondition(ConditionType.Entry, price));
        return this;
    }
    
    public StrategyBuilder Breakout(double? level = null)
    {
        _strategy.EntryConditions.Add(new PatternCondition(PatternType.Breakout, level));
        return this;
    }
    
    public StrategyBuilder Pullback(double? level = null)
    {
        _strategy.EntryConditions.Add(new PatternCondition(PatternType.Pullback, level));
        return this;
    }
    
    public StrategyBuilder IsAboveVwap()
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapAbove));
        return this;
    }
    
    public StrategyBuilder IsBelowVwap()
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapBelow));
        return this;
    }
    
    public StrategyBuilder IsEmaAbove(int period)
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.EmaAbove, period));
        return this;
    }
    
    public StrategyBuilder IsEmaBelow(int period)
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.EmaBelow, period));
        return this;
    }
    
    public StrategyBuilder IsDiPositive()
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.DiPositive));
        return this;
    }
    
    public StrategyBuilder IsDiNegative()
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.DiNegative));
        return this;
    }
    
    public StrategyBuilder IsAdxAbove(double threshold)
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.AdxAbove, threshold));
        return this;
    }
    
    public StrategyBuilder IsRsiOversold(double threshold = 30)
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.RsiOversold, threshold));
        return this;
    }
    
    public StrategyBuilder IsRsiOverbought(double threshold = 70)
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.RsiOverbought, threshold));
        return this;
    }
    
    public StrategyBuilder IsMacdBullish()
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.MacdBullish));
        return this;
    }
    
    public StrategyBuilder IsMacdBearish()
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.MacdBearish));
        return this;
    }
    
    public StrategyBuilder IsGapUp(double minPercent = 3)
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.GapUp, minPercent));
        return this;
    }
    
    public StrategyBuilder IsGapDown(double minPercent = 3)
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.GapDown, minPercent));
        return this;
    }
    
    public StrategyBuilder IsVolumeAbove(double multiplier)
    {
        _strategy.EntryConditions.Add(new IndicatorCondition(IndicatorType.VolumeAbove, multiplier));
        return this;
    }

    /// <summary>
    /// Price must hold above this level (used for support confirmation).
    /// Example: HoldsAbove(0.48) - price must stay above $0.48
    /// </summary>
    public StrategyBuilder HoldsAbove(double price)
    {
        _strategy.EntryConditions.Add(new PriceLevelCondition(PriceLevelType.HoldsAbove, price));
        return this;
    }

    /// <summary>
    /// Price must hold below this level (used for resistance confirmation in shorts).
    /// Example: HoldsBelow(150) - price must stay below $150
    /// </summary>
    public StrategyBuilder HoldsBelow(double price)
    {
        _strategy.EntryConditions.Add(new PriceLevelCondition(PriceLevelType.HoldsBelow, price));
        return this;
    }

    /// <summary>
    /// Price must be near a specific level (within tolerance %).
    /// Example: IsNear(3.68, 1.0) - price within 1% of $3.68
    /// </summary>
    public StrategyBuilder IsNear(double price, double tolerancePercent = 1.0)
    {
        _strategy.EntryConditions.Add(new PriceLevelCondition(PriceLevelType.Near, price, tolerancePercent));
        return this;
    }

    // ========================================
    // ORDER DIRECTION
    // ========================================
    
    public StrategyBuilder Order(TradeDirection direction = TradeDirection.Long)
    {
        _strategy.Direction = direction;
        return this;
    }
    
    public StrategyBuilder Long() => Order(TradeDirection.Long);
    public StrategyBuilder Short() => Order(TradeDirection.Short);
    
    // ========================================
    // EXIT CONDITIONS
    // ========================================
    
    public StrategyBuilder TakeProfit(double price)
    {
        _strategy.TakeProfitPrice = price;
        return this;
    }

    /// <summary>
    /// Sets multiple take profit targets for scaling out.
    /// Example: TakeProfit(5.00, 6.50, 8.00) - T1: $5, T2: $6.50, T3: $8
    /// </summary>
    public StrategyBuilder TakeProfit(double t1, double t2, double? t3 = null)
    {
        _strategy.TakeProfitTargets.Clear();
        _strategy.TakeProfitTargets.Add(new TakeProfitTarget { Price = t1, PercentToSell = t3.HasValue ? 33 : 50, Label = "T1" });
        _strategy.TakeProfitTargets.Add(new TakeProfitTarget { Price = t2, PercentToSell = t3.HasValue ? 33 : 50, Label = "T2" });
        if (t3.HasValue)
            _strategy.TakeProfitTargets.Add(new TakeProfitTarget { Price = t3.Value, PercentToSell = 34, Label = "T3" });
        _strategy.TakeProfitPrice = t1; // Primary target for simple exits
        return this;
    }

    /// <summary>
    /// Adds a specific take profit target with custom percentage to sell.
    /// Example: AddTarget(5.00, 50, "T1") - sell 50% at $5
    /// </summary>
    public StrategyBuilder AddTarget(double price, int percentToSell, string? label = null)
    {
        _strategy.TakeProfitTargets.Add(new TakeProfitTarget 
        { 
            Price = price, 
            PercentToSell = percentToSell, 
            Label = label ?? $"T{_strategy.TakeProfitTargets.Count + 1}" 
        });
        if (!_strategy.TakeProfitPrice.HasValue)
            _strategy.TakeProfitPrice = price;
        return this;
    }

    public StrategyBuilder TakeProfitPercent(double percent)
    {
        _strategy.TakeProfitPercent = percent;
        return this;
    }
    
    public StrategyBuilder StopLoss(double price)
    {
        _strategy.StopLossPrice = price;
        return this;
    }
    
    public StrategyBuilder StopLossPercent(double percent)
    {
        _strategy.StopLossPercent = percent;
        return this;
    }
    
    public StrategyBuilder TrailingStopLoss(double percent)
    {
        _strategy.TrailingStopPercent = percent;
        return this;
    }
    
    public StrategyBuilder ExitStrategy(TimeSpan timeOfDay)
    {
        _strategy.ExitTime = timeOfDay;
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
        if (_strategy.EntryConditions.Count == 0)
            throw new InvalidOperationException("Then() requires a preceding condition (e.g. .IsAboveVwap().Then(...))");

        // Pop the last condition to use as the "if" condition
        var condition = _strategy.EntryConditions[^1];
        _strategy.EntryConditions.RemoveAt(_strategy.EntryConditions.Count - 1);

        var block = new ConditionalBlock();
        _strategy.ConditionalBlocks.Add(block);

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
        _strategy.IsAutonomous = true;
        return this;
    }
    
    public StrategyBuilder AdaptiveOrder()
    {
        _strategy.IsAdaptive = true;
        return this;
    }
    
    public StrategyBuilder Repeat()
    {
        _strategy.ShouldRepeat = true;
        return this;
    }
    
    // ========================================
    // BUILD
    // ========================================
    
    public StrategyDefinition Build() => _strategy;
    
    /// <summary>
    /// Serializes the strategy to IdiotScript text format.
    /// </summary>
    public string ToScript()
    {
        var parts = new List<string> { $"Ticker({_strategy.Symbol})" };
        
        if (!string.IsNullOrEmpty(_strategy.Name))
            parts.Add($"Name(\"{_strategy.Name}\")");
        
        if (_strategy.Session != TradingSession.RTH)
            parts.Add($"Session(IS.{_strategy.Session.ToString().ToUpperInvariant()})");
        
        if (_strategy.Quantity > 0)
            parts.Add($"Quantity({_strategy.Quantity})");
        
        // Entry conditions
        foreach (var cond in _strategy.EntryConditions)
        {
            parts.Add(cond.ToScript());
        }

        // Conditional blocks
        foreach (var block in _strategy.ConditionalBlocks)
        {
            parts.Add(block.ToScript());
        }

        // Direction
        if (_strategy.Direction == TradeDirection.Short)
            parts.Add("Short()");
        else
            parts.Add("Long()");

        // Exit conditions
        if (_strategy.TakeProfitPrice.HasValue)
            parts.Add($"TakeProfit({_strategy.TakeProfitPrice})");
        if (_strategy.StopLossPrice.HasValue)
            parts.Add($"StopLoss({_strategy.StopLossPrice})");
        if (_strategy.TrailingStopPercent.HasValue)
            parts.Add($"TrailingStopLoss({_strategy.TrailingStopPercent})");
        
        // Advanced
        if (_strategy.IsAutonomous)
            parts.Add("AutonomousTrading()");
        if (_strategy.IsAdaptive)
            parts.Add("AdaptiveOrder()");
        if (_strategy.ShouldRepeat)
            parts.Add("Repeat()");
        
        return string.Join("\n    .", parts);
    }
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
    public int Quantity { get; set; }
    
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

public interface ICondition
{
    string ToScript();
    bool Evaluate(IndicatorSnapshot indicators);
}

public enum ConditionType { Entry, Breakout, Pullback }
public enum PatternType { Breakout, Pullback }
public enum IndicatorType
{
    VwapAbove, VwapBelow,
    EmaAbove, EmaBelow,
    DiPositive, DiNegative,
    AdxAbove,
    RsiOversold, RsiOverbought,
    MacdBullish, MacdBearish,
    GapUp, GapDown,
    VolumeAbove
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
    public bool Evaluate(IndicatorSnapshot indicators) => true; // Evaluated by pattern engine
}

public sealed class IndicatorCondition(IndicatorType type, double? parameter = null) : ICondition
{
    public IndicatorType Type { get; } = type;
    public double? Parameter { get; } = parameter;

    public string ToScript() => Parameter.HasValue 
        ? $"Is{Type}({Parameter})" 
        : $"Is{Type}()";

    public bool Evaluate(IndicatorSnapshot indicators) => Type switch
    {
        IndicatorType.VwapAbove => indicators.VwapDistance > 0,
        IndicatorType.VwapBelow => indicators.VwapDistance < 0,
        IndicatorType.EmaAbove => indicators.Price > (indicators.Ema9 ?? 0),
        IndicatorType.EmaBelow => indicators.Price < (indicators.Ema9 ?? 0),
        IndicatorType.DiPositive => indicators.IsBullishTrend,
        IndicatorType.DiNegative => !indicators.IsBullishTrend,
        IndicatorType.AdxAbove => indicators.Adx >= (Parameter ?? 25),
        IndicatorType.RsiOversold => indicators.Rsi <= (Parameter ?? 30),
        IndicatorType.RsiOverbought => indicators.Rsi >= (Parameter ?? 70),
        IndicatorType.MacdBullish => indicators.IsMacdBullish,
        IndicatorType.MacdBearish => !indicators.IsMacdBullish,
        IndicatorType.VolumeAbove => indicators.VolumeRatio >= (Parameter ?? 1.5),
        _ => true
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
    private bool _hasViolated = false;
    private double _lowestSeen = double.MaxValue;
    private double _highestSeen = double.MinValue;

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

        // Track extremes
        if (price < _lowestSeen) _lowestSeen = price;
        if (price > _highestSeen) _highestSeen = price;

        return Type switch
        {
            // HoldsAbove: True if price is currently above AND has never gone significantly below
            PriceLevelType.HoldsAbove => price >= Level && _lowestSeen >= Level * 0.995, // 0.5% tolerance

            // HoldsBelow: True if price is currently below AND has never gone significantly above
            PriceLevelType.HoldsBelow => price <= Level && _highestSeen <= Level * 1.005,

            // Near: True if price is within tolerance % of level
            PriceLevelType.Near => Math.Abs((price - Level) / Level * 100) <= TolerancePercent,

            // BreaksAbove: True if price breaks above level
            PriceLevelType.BreaksAbove => price > Level && _highestSeen > Level,

            // BreaksBelow: True if price breaks below level
            PriceLevelType.BreaksBelow => price < Level && _lowestSeen < Level,

            _ => true
        };
    }

    /// <summary>
    /// Resets tracking state (call when strategy resets).
    /// </summary>
    public void Reset()
    {
        _hasViolated = false;
        _lowestSeen = double.MaxValue;
        _highestSeen = double.MinValue;
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
    public ICondition IsEmaAbove(int period) => new IndicatorCondition(IndicatorType.EmaAbove, period);
    public ICondition IsEmaBelow(int period) => new IndicatorCondition(IndicatorType.EmaBelow, period);
    public ICondition IsDiPositive() => new IndicatorCondition(IndicatorType.DiPositive);
    public ICondition IsDiNegative() => new IndicatorCondition(IndicatorType.DiNegative);
    public ICondition IsAdxAbove(double threshold) => new IndicatorCondition(IndicatorType.AdxAbove, threshold);
    public ICondition IsRsiOversold(double threshold = 30) => new IndicatorCondition(IndicatorType.RsiOversold, threshold);
    public ICondition IsRsiOverbought(double threshold = 70) => new IndicatorCondition(IndicatorType.RsiOverbought, threshold);
    public ICondition IsMacdBullish() => new IndicatorCondition(IndicatorType.MacdBullish);
    public ICondition IsMacdBearish() => new IndicatorCondition(IndicatorType.MacdBearish);
    public ICondition IsGapUp(double minPercent = 3) => new IndicatorCondition(IndicatorType.GapUp, minPercent);
    public ICondition IsGapDown(double minPercent = 3) => new IndicatorCondition(IndicatorType.GapDown, minPercent);
    public ICondition IsVolumeAbove(double multiplier) => new IndicatorCondition(IndicatorType.VolumeAbove, multiplier);
    public ICondition HoldsAbove(double price) => new PriceLevelCondition(PriceLevelType.HoldsAbove, price);
    public ICondition HoldsBelow(double price) => new PriceLevelCondition(PriceLevelType.HoldsBelow, price);
    public ICondition IsNear(double price, double tolerance = 1.0) => new PriceLevelCondition(PriceLevelType.Near, price, tolerance);
    public ICondition BreaksAbove(double price) => new PriceLevelCondition(PriceLevelType.BreaksAbove, price);
    public ICondition BreaksBelow(double price) => new PriceLevelCondition(PriceLevelType.BreaksBelow, price);
    public ICondition Breakout(double? level = null) => new PatternCondition(PatternType.Breakout, level);
    public ICondition Pullback(double? level = null) => new PatternCondition(PatternType.Pullback, level);
}

/// <summary>
/// Fluent builder for chaining ElseIf/Else after a Then().
/// Returns to StrategyBuilder when the conditional block is complete.
/// </summary>
public sealed class ConditionalBuilder
{
    private readonly StrategyBuilder _parent;
    private readonly ConditionalBlock _block;

    internal ConditionalBuilder(StrategyBuilder parent, ConditionalBlock block)
    {
        _parent = parent;
        _block = block;
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
        _block.Branches.Add(new ConditionalBranch { Condition = cond, Overrides = builder.Overrides });
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
        _block.Branches.Add(new ConditionalBranch { Condition = null, Overrides = builder.Overrides });
        return _parent;
    }

    /// <summary>
    /// Ends the conditional block without an Else branch.
    /// Returns to StrategyBuilder for continued chaining.
    /// </summary>
    public StrategyBuilder EndIf() => _parent;

    // Delegate common terminal methods to allow chaining without EndIf()
    public StrategyBuilder StopLoss(double price) => _parent.StopLoss(price);
    public StrategyBuilder StopLossPercent(double percent) => _parent.StopLossPercent(percent);
    public StrategyBuilder TrailingStopLoss(double percent) => _parent.TrailingStopLoss(percent);
    public StrategyBuilder Repeat() => _parent.Repeat();
    public StrategyBuilder AutonomousTrading() => _parent.AutonomousTrading();
    public StrategyBuilder AdaptiveOrder() => _parent.AdaptiveOrder();
    public StrategyDefinition Build() => _parent.Build();
    public string ToScript() => _parent.ToScript();
}
