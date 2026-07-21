using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// Header row for one 3:55 AM ET auto-gapper scan (IP: automated gapper
/// discovery). Exactly one row per ET trading day — its existence is ALSO the
/// once-per-day idempotency guard the Monitor checks so the scan fires once even
/// at 1s tick cadence and survives a process restart mid-window. Children
/// (<see cref="AutoGapperCandidate"/>) capture every screened ticker's feature
/// vector so a future model can learn which gappers to arm. No FK to the user —
/// this is a permanent research record that must outlive any strategy it armed.
/// </summary>
public sealed class AutoGapperScan
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>ET calendar date "yyyy-MM-dd".</summary>
    [MaxLength(10)] public string ScanEtDate { get; set; } = "";

    /// <summary>
    /// Which pass produced this scan: "auto" (the scheduled premarket job — at
    /// most one per ET day, the idempotency key) or "manual" (an operator
    /// pre-arm / CLI run, which may repeat and never blocks the auto pass).
    /// </summary>
    [MaxLength(16)] public string Phase { get; set; } = "auto";

    public DateTime ScanStartedUtc { get; set; }
    public DateTime? ScanCompletedUtc { get; set; }

    public int MoversScreened { get; set; }
    public int Qualified { get; set; }
    public int Armed { get; set; }
    public int Skipped { get; set; }

    public double MinGapPercent { get; set; }
    public int MaxCount { get; set; }
    [MaxLength(16)] public string BrokerMode { get; set; } = "";
    [MaxLength(400)] public string? Note { get; set; }
}

/// <summary>
/// One screened gapper candidate and the full decision record — the ML feature
/// store for auto-gapper predictions. Captures the raw signals available at
/// 3:55 AM (price, gap %, volume ratio, ATR/volatility), the ADAPTIVE parameters
/// the synthesizer derived from them, and the outcome of the arm decision
/// (armed + StrategyId, or the skip reason). Join back to the TradeDiary via
/// StrategyId to attach realized P&amp;L labels later. FK-linked to its scan.
/// </summary>
public sealed class AutoGapperCandidate
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScanId { get; set; }

    [MaxLength(10)] public string ScanEtDate { get; set; } = "";
    [MaxLength(16)] public string Symbol { get; set; } = "";
    public DateTime CapturedUtc { get; set; }

    // ── Raw signals at scan time (the feature vector) ──
    public double Price { get; set; }
    public double? PreviousClose { get; set; }
    public double GapPercent { get; set; }
    public long? PremarketVolume { get; set; }
    public double? AvgDailyVolume { get; set; }
    public double? VolumeRatio { get; set; }
    public double? AtrPercent { get; set; }
    /// <summary>Conviction rank score (gap × liquidity) used to pick the top N.</summary>
    public double Score { get; set; }
    public int Rank { get; set; }

    // ── Adaptive parameters the synthesizer chose ──
    [MaxLength(24)] public string BehaviorClass { get; set; } = "";
    public double StopLossPercent { get; set; }
    public double? TrailingStopPercent { get; set; }
    public double PeakGivebackPercent { get; set; }
    [MaxLength(8)] public string ArmExitEt { get; set; } = "";
    [MaxLength(8)] public string SellByEt { get; set; } = "";
    public double MinVolumeRatio { get; set; }
    public double PriceBandLow { get; set; }
    public double PriceBandHigh { get; set; }
    public decimal Notional { get; set; }

    // ── Decision outcome ──
    public bool Armed { get; set; }
    public Guid? StrategyId { get; set; }
    [MaxLength(48)] public string? SkipReason { get; set; }
}
