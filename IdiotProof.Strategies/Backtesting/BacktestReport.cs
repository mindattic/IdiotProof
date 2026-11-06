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
    /// True when the strategy uses .If/.Then branching. Branches resolve per bar as in the
    /// Monitor; a trade keeps the branch it entered on.
    /// </summary>
    public bool HasBranching { get; init; }

    /// <summary>Human-readable caveats about this run (e.g. unsupported features).</summary>
    public string? Note { get; set; }

    /// <summary>How many steps the strategy has (1 for every single-step strategy).</summary>
    public int StepCount { get; init; } = 1;

    /// <summary>Each time a sequenced strategy moved from one step to the next (ThenWhen held while flat).</summary>
    public List<StepTransition> StepTransitions { get; } = [];

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

    /// <summary>
    /// Per-bar condition pass/fail table. Populated during replay for the UI's
    /// condition progress view. Empty if the run produced no bars or no conditions.
    /// </summary>
    public List<CandleConditionRow> ConditionTable { get; } = [];
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

/// <summary>A sequenced strategy advancing to its next step, on the bar whose snapshot satisfied ThenWhen.</summary>
public sealed record StepTransition(DateTime Utc, int FromStep, int ToStep);

/// <summary>A simulated round-trip trade with its scale-out fills.</summary>
public sealed class BacktestTrade
{
    public int Cycle { get; init; }

    /// <summary>Which step of the sequence opened this trade (0-based; 0 for a single-step strategy).</summary>
    public int Step { get; init; }
    public string? StepName { get; init; }

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

    /// <summary>
    /// High-water mark since entry (long-shaped), maintained bar-by-bar during
    /// the replay. Feeds the trailing-stop and peak-giveback exits; seeded at
    /// the entry price (same floor rule as the live GapperExitEvaluator).
    /// </summary>
    public decimal PeakSinceEntry { get; set; }
}

/// <summary>A single exit fill (a scale-out target, a stop, a time/EOD close).</summary>
public sealed class BacktestFill
{
    public DateTime Utc { get; init; }
    public decimal Price { get; init; }
    public int Shares { get; init; }
    public string Reason { get; init; } = "";
}

/// <summary>
/// Per-bar snapshot of each entry condition's pass/fail state during a replay.
/// Used to render the condition progress table in the Backtest UI (IP-US-J2).
/// </summary>
public sealed class CandleConditionRow
{
    public DateTime Utc { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }

    /// <summary>Condition labels in the same order as <see cref="Results"/>.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>Satisfied state of each entry condition on this bar.</summary>
    public IReadOnlyList<bool> Results { get; init; } = [];

    /// <summary>True if ALL entry conditions were satisfied on this bar.</summary>
    public bool AllSatisfied { get; init; }

    /// <summary>True if a new trigger fired (became true for the first time) on this bar.</summary>
    public bool HasTriggerFire { get; init; }

    /// <summary>True if this bar opened a new position.</summary>
    public bool OpenedTrade { get; init; }

    /// <summary>True if the strategy was already in a position at the start of this bar.</summary>
    public bool InPosition { get; init; }

    /// <summary>The active step on this bar (0-based). While a sequence waits between steps the labels are its ThenWhen conditions.</summary>
    public int Step { get; init; }

    /// <summary>True while the sequence is flat after a step's exit and watching that step's ThenWhen.</summary>
    public bool AwaitingTransition { get; init; }

    /// <summary>Per ConditionalBlock, the index of the branch that matched on this bar (-1 when none matched).</summary>
    public IReadOnlyList<int> BranchTaken { get; init; } = [];

    /// <summary>Per ConditionalBlock, each branch condition's result on this bar (an Else branch is always true).</summary>
    public IReadOnlyList<IReadOnlyList<bool>> BranchConditionResults { get; init; } = [];

    /// <summary>Key indicator values as of this bar (price, VWAP, EMAs, RSI, ATR, volume), for fork hover detail.</summary>
    public IReadOnlyDictionary<string, double> Indicators { get; init; } = new Dictionary<string, double>();
}

/// <summary>Knobs for the replay. Defaults match the sample strategies.</summary>
public sealed class BacktestOptions
{
    /// <summary>Share count used when the strategy doesn't pin one (Quantity==0, not notional).</summary>
    public int DefaultQuantity { get; init; } = 100;

    /// <summary>
    /// Previous trading day's official close — the reference for gap math.
    /// Without it every IsGapUp/IsGapDown/IsGapBetween condition fails closed
    /// (same as live), so a gap strategy replayed without this can never
    /// trigger no matter what the day actually did.
    /// </summary>
    public decimal? PreviousClose { get; init; }

    /// <summary>
    /// Leader context for LeaderConfirms / NotLeaderDiverging. Without it both guards fail closed
    /// (same as live), so a leader-gated strategy can never fire.
    /// </summary>
    public BacktestLeaderContext? Leaders { get; init; }
}
