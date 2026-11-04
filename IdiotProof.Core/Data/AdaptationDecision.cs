using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Core.Data;

/// <summary>
/// One intraday adaptation decision for an armed strategy (IP-US-S4): the measured features, every
/// variant's shadow record and posterior, the variant chosen, whether the Risk Guardian let it through,
/// the script it replaced (so it can be reverted), and, once trades close, the realized outcome joined
/// from the TradeDiary. A permanent research record: no FK, so it outlives the strategy.
/// </summary>
public sealed class AdaptationDecision
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid StrategyId { get; set; }
    public Guid OwnerUserId { get; set; }
    [MaxLength(16)] public string Symbol { get; set; } = "";

    /// <summary>ET calendar date "yyyy-MM-dd".</summary>
    [MaxLength(10)] public string EtDate { get; set; } = "";
    public DateTime DecidedUtc { get; set; }
    public int ClosedBars { get; set; }

    /// <summary>applied | unchanged | risk-blocked.</summary>
    [MaxLength(16)] public string Outcome { get; set; } = "";
    [MaxLength(400)] public string? Reason { get; set; }

    /// <summary>Derived parameters + regime reading, JSON.</summary>
    public string FeaturesJson { get; set; } = "";

    /// <summary>Every variant with wins, losses, mean return, Beta posterior and P(best), JSON.</summary>
    public string VariantsJson { get; set; } = "";
    [MaxLength(64)] public string ChosenVariant { get; set; } = "";
    [MaxLength(400)] public string? AppliedOverrides { get; set; }
    [MaxLength(400)] public string? RiskSummary { get; set; }
    public string? OriginalScriptJson { get; set; }

    public int? RealizedTrades { get; set; }
    public decimal? RealizedPnL { get; set; }
    public decimal? RealizedReturnPercent { get; set; }
    public DateTime? OutcomeRecordedUtc { get; set; }
}
