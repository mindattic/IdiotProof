using IdiotProof.Models;

namespace IdiotProof.Strategies.Backtesting;

/// <summary>
/// Result of replaying a DSL strategy over one day (or any candle window). Carries
/// the trigger timeline (when each entry condition fired), the simulated trades, and
/// a P&amp;L summary so the UI can answer "did my triggers go off, and what were the
/// numbers" without re-deriving anything.
/// </summary>
public sealed class BacktestReport
{
    public string Symbol { get; init; } = "";
    public string StrategyName { get; init; } = "";
    public DateOnly Date { get; init; }
    public int BarsProcessed { get; init; }

    /// <summary>The candle window's first and last bar timestamps (UTC).</summary>
    public DateTime? FirstBarUtc { get; init; }
    public DateTime? LastBarUtc { get; init; }

    /// <summary>
    /// Each time one of the strategy's entry conditions first became true (per entry
    /// cycle). This is the "did the triggers go off correctly" timeline.
    /// </summary>
    public List<TriggerFire> Triggers { get; } = [];

    /// <summary>The simulated round-trip trades, in entry order.</summary>
    public List<BacktestTrade> Trades { get; } = [];

    /// <summary>
    /// True when the strategy uses .If/.Then branching. Branch resolution is not yet
    /// simulated in the backtest (see <see cref="Note"/>); only base entry conditions run.
    /// </summary>
    public bool HasBranching { get; init; }

    /// <summary>Human-readable caveats about this run (e.g. unsupported features).</summary>
    public string? Note { get; set; }

    // ---- Summary (computed) ----
    public int TradeCount => Trades.Count;
    public int Wins => Trades.Count(t => t.PnL > 0m);
    public int Losses => Trades.Count(t => t.PnL < 0m);
    public decimal TotalPnL => Trades.Sum(t => t.PnL);
    public decimal WinRate => TradeCount > 0 ? (decimal)Wins / TradeCount : 0m;
    public decimal BestTrade => Trades.Count > 0 ? Trades.Max(t => t.PnL) : 0m;
    public decimal WorstTrade => Trades.Count > 0 ? Trades.Min(t => t.PnL) : 0m;

    /// <summary>True if no entry condition ever fired — the "no setup today" case.</summary>
    public bool NoTriggersFired => Triggers.Count == 0;
}

/// <summary>One entry condition becoming true during the replay.</summary>
public sealed class TriggerFire
{
    public DateTime Utc { get; init; }
    public string Condition { get; init; } = "";
    public decimal Price { get; init; }

    /// <summary>Which entry cycle this fire belongs to (1-based; increments on .Repeat()).</summary>
    public int Cycle { get; init; }

    /// <summary>True if this fire was the bar that completed the setup and opened a position.</summary>
    public bool OpenedPosition { get; init; }
}

/// <summary>A simulated round-trip trade with its scale-out fills.</summary>
public sealed class BacktestTrade
{
    public int Cycle { get; init; }
    public TradeDirection Direction { get; init; }
    public DateTime EntryUtc { get; init; }
    public decimal EntryPrice { get; init; }
    public int Quantity { get; init; }
    public decimal StopPrice { get; init; }

    public List<BacktestFill> Exits { get; } = [];
    public decimal PnL { get; set; }
    public decimal ReturnPercent { get; set; }
    public string ExitReason { get; set; } = "";
    public DateTime? ExitUtc => Exits.Count > 0 ? Exits[^1].Utc : null;
}

/// <summary>A single exit fill (a scale-out target, a stop, a time/EOD close).</summary>
public sealed class BacktestFill
{
    public DateTime Utc { get; init; }
    public decimal Price { get; init; }
    public int Shares { get; init; }
    public string Reason { get; init; } = "";
}

/// <summary>Knobs for the replay. Defaults match the sample strategies.</summary>
public sealed class BacktestOptions
{
    /// <summary>Share count used when the strategy doesn't pin one (Quantity==0, not notional).</summary>
    public int DefaultQuantity { get; init; } = 100;
}
