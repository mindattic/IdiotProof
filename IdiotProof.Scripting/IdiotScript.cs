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
    public double? StopLossPrice { get; set; }
    public double? StopLossPercent { get; set; }
    public double? TrailingStopPercent { get; set; }
    public TimeSpan? ExitTime { get; set; }
    
    public bool IsAutonomous { get; set; }
    public bool IsAdaptive { get; set; }
    public bool ShouldRepeat { get; set; }
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
