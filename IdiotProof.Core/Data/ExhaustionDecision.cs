using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Core.Data;

/// <summary>
/// One intraday exhaustion verdict (IP-US-S2/S4): the extreme it saw, the counted reversal and trap
/// rates it rested on, the per-ticker exits it would use, and whether it armed a paper strategy,
/// skipped, or (fade shorts) recorded the call for analysis only. Joined to the TradeDiary through
/// StrategyId for the realized-P&amp;L label. No FK, so it outlives the strategy.
/// </summary>
public sealed class ExhaustionDecisionRow
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OwnerUserId { get; set; }
    [MaxLength(16)] public string Symbol { get; set; } = "";

    /// <summary>ET calendar date "yyyy-MM-dd".</summary>
    [MaxLength(10)] public string EtDate { get; set; } = "";
    public DateTime DecidedUtc { get; set; }

    /// <summary>None | Long | Short.</summary>
    [MaxLength(8)] public string Side { get; set; } = "";
    public bool Armed { get; set; }

    /// <summary>True for a call that cleared every bar but is never armed (fade shorts today).</summary>
    public bool AnalysisOnly { get; set; }
    public Guid? StrategyId { get; set; }
    [MaxLength(400)] public string Reason { get; set; } = "";

    public int ClosedBars { get; set; }
    public double MoveAtr { get; set; }
    public double ExtremePrice { get; set; }
    public double TriggerPrice { get; set; }

    public int? ReversalSuccesses { get; set; }
    public int? ReversalSamples { get; set; }
    public double? ReversalProbability { get; set; }
    public double? ReversalLow { get; set; }
    public double? ReversalHigh { get; set; }

    [MaxLength(32)] public string? TrapDetector { get; set; }
    public double? TrapProbability { get; set; }
    public int? TrapSamples { get; set; }

    public double? StopLossPercent { get; set; }
    public double? TrailingStopPercent { get; set; }
    public double? TakeProfitPercent { get; set; }
    public double? AtrPercent { get; set; }
    [MaxLength(16)] public string? Regime { get; set; }

    /// <summary>The trap detector's feature vector and bucket, JSON.</summary>
    public string FeaturesJson { get; set; } = "";
}
