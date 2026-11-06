using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Core.Data;

/// <summary>T1: one raw (unadjusted) daily bar per symbol-session for every Alpaca us_equity asset, active or inactive.</summary>
public sealed class GapperStudyDailyBar
{
    public long Id { get; set; }
    [MaxLength(16)] public string Symbol { get; set; } = "";
    public DateOnly DateEt { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public long TradeCount { get; set; }
    public decimal Vwap { get; set; }
    [MaxLength(16)] public string Feed { get; set; } = "";
}

/// <summary>T6: forward and reverse splits by ex-date, applied in-house to prior closes and ADV shares.</summary>
public sealed class GapperStudySplit
{
    public long Id { get; set; }
    [MaxLength(16)] public string Symbol { get; set; } = "";
    public DateOnly ExDate { get; set; }
    public decimal OldRate { get; set; }
    public decimal NewRate { get; set; }
}

/// <summary>The asset list the universe was built from, plus T5's ticker to CIK mapping and how it was found.</summary>
public sealed class GapperStudyCikMap
{
    [Key, MaxLength(16)] public string Symbol { get; set; } = "";
    [MaxLength(200)] public string Name { get; set; } = "";
    public bool Active { get; set; }
    public bool IsAdr { get; set; }
    [MaxLength(16)] public string? Cik { get; set; }
    /// <summary>company_tickers.json | form4IssuerTradingSymbol | exactIssuerName.</summary>
    [MaxLength(32)] public string? CikSource { get; set; }
    public DateTime? MappedUtc { get; set; }
}

/// <summary>A completed unit of a resumable pull (a symbol's daily bars, a news window, an EDGAR quarter, a header fetch).</summary>
public sealed class GapperStudyCoverage
{
    public long Id { get; set; }
    [MaxLength(24)] public string Tier { get; set; } = "";
    [MaxLength(120)] public string Key { get; set; } = "";
    public int Count { get; set; }
    public DateTime CompletedUtc { get; set; }
}

/// <summary>
/// One symbol-day of U(D) (or an excluded one, with the reason): the point-in-time facts from daily
/// bars dated before D, the candidate flag from D's own bar, and, once minute bars are in, the gapper
/// tier and control assignment.
/// </summary>
public sealed class GapperStudyUniverseDay
{
    public long Id { get; set; }
    public DateOnly DateEt { get; set; }
    [MaxLength(16)] public string Symbol { get; set; } = "";
    /// <summary>train | validation | holdout | smoke | none.</summary>
    [MaxLength(16)] public string Split { get; set; } = "";
    public decimal AdjPrevClose { get; set; }
    public decimal Adv20Dollars { get; set; }
    public decimal Adv20Shares { get; set; }
    public int Sessions { get; set; }
    public int AdvDecile { get; set; }
    public int PriceTier { get; set; }
    public bool IsAdr { get; set; }
    public bool IsCandidate { get; set; }
    public decimal? DayHigh { get; set; }
    public decimal? DayVolume { get; set; }
    public bool IsGapper { get; set; }
    /// <summary>Highest qualifying tier (Tier20 | Tier50 | Tier100); null when none qualifies.</summary>
    [MaxLength(8)] public string? GapTier { get; set; }
    /// <summary>premarket | open.</summary>
    [MaxLength(12)] public string? GapperSubType { get; set; }
    public double? GapMax { get; set; }
    public decimal? PremarketDollarVolumeAtTier { get; set; }
    /// <summary>Set on the control day's row: the gapper symbol this row controls for.</summary>
    [MaxLength(16)] public string? ControlForSymbol { get; set; }
    [MaxLength(32)] public string? ExcludedReason { get; set; }
}

/// <summary>One row per gapper day: what every tier touch looked like, with the premarket dollar-volume floor applied per tier.</summary>
public sealed class GapperStudyEvent
{
    public long Id { get; set; }
    [MaxLength(16)] public string Symbol { get; set; } = "";
    public DateOnly DateEt { get; set; }
    [MaxLength(16)] public string Split { get; set; } = "";
    public decimal AdjPrevClose { get; set; }
    public double GapMax { get; set; }
    public bool Tier20Touched { get; set; }
    public bool Tier50Touched { get; set; }
    public bool Tier100Touched { get; set; }
    public bool Tier20 { get; set; }
    public bool Tier50 { get; set; }
    public bool Tier100 { get; set; }
    public TimeOnly? Tier20TouchTimeEt { get; set; }
    public TimeOnly? Tier50TouchTimeEt { get; set; }
    public TimeOnly? Tier100TouchTimeEt { get; set; }
    public decimal? PmDollarVolumeAtTier20 { get; set; }
    public decimal? PmDollarVolumeAtTier50 { get; set; }
    public decimal? PmDollarVolumeAtTier100 { get; set; }
    [MaxLength(12)] public string? SubType { get; set; }
    public decimal PmHigh { get; set; }
    public TimeOnly? PmHighTimeEt { get; set; }
    public decimal? RthHigh { get; set; }
    public TimeOnly? HodTimeEt { get; set; }
    public decimal PmDollarVolume { get; set; }
    public int MinuteBars { get; set; }
    public double SipShare { get; set; }
}

/// <summary>
/// A <see cref="IdiotProof.Evidence.Gappers.GapperFormationSnapshot"/> as a row. Shared by the study
/// (<c>Source = "backtest"</c>) and the live formation recorder (<c>Source = "live"</c>); the
/// column names are the record's property names so the two never drift.
/// </summary>
public sealed class GapperFormationSnapshotRow
{
    public long Id { get; set; }
    [MaxLength(12)] public string Source { get; set; } = "";
    [MaxLength(16)] public string Symbol { get; set; } = "";
    public DateOnly DateEt { get; set; }
    /// <summary>The schedule label ("D-1 16:00", "08:30").</summary>
    [MaxLength(12)] public string DecisionLabel { get; set; } = "";
    public TimeOnly DecisionTimeEt { get; set; }
    public DateTime DecisionUtc { get; set; }
    [MaxLength(16)] public string Split { get; set; } = "";
    public int FeatureVersion { get; set; }
    public bool IsCandidate { get; set; }
    public bool IsControl { get; set; }

    public double? GapPct { get; set; }
    public double PmCumVol { get; set; }
    public double? VolRatioTod { get; set; }
    public double? OwnProfileRatio { get; set; }
    public double? Velocity1m { get; set; }
    public double? Velocity3m { get; set; }
    public double? Velocity5m { get; set; }
    public double? Velocity15m { get; set; }
    public double? Acceleration { get; set; }
    public double? VolumeRoc { get; set; }
    public int TradesProxy { get; set; }
    public double? SpreadProxy { get; set; }
    public double? DistToPmHighPct { get; set; }
    public double? MinsSinceFirstAbnormal { get; set; }
    public double? SessionVwap { get; set; }
    public double? AbnormalReturnD1 { get; set; }
    public double? AbnormalReturnD2 { get; set; }
    public double? AbnormalReturnD3 { get; set; }
    public double? AbnormalReturnD4 { get; set; }
    public double? AbnormalReturnD5 { get; set; }
    public double? VolumeRatioD1 { get; set; }
    public double? VolumeRatioD2 { get; set; }
    public double? VolumeRatioD3 { get; set; }
    public double? VolumeRatioD4 { get; set; }
    public double? VolumeRatioD5 { get; set; }
    public int? DaysSinceReverseSplit { get; set; }
    public int? DaysSince424B { get; set; }
    public int? DaysSinceS1 { get; set; }
    public double? CatalystScore { get; set; }
    public double? CatalystAgeMinutes { get; set; }
    public int? CatalystDirection { get; set; }
    [MaxLength(24)] public string? CatalystPreClass { get; set; }
    public bool? CatalystStampIsUpperBound { get; set; }
    public bool? Halted { get; set; }

    /// <summary>JSON object of the literature add-ons (<c>Extra</c>).</summary>
    public string ExtraJson { get; set; } = "{}";
    public DateTime ComputedUtc { get; set; }
}

/// <summary>The protocol's labels for one (symbol, day, decision time); computed once, never redefined.</summary>
public sealed class GapperStudyLabel
{
    public long Id { get; set; }
    [MaxLength(16)] public string Symbol { get; set; } = "";
    public DateOnly DateEt { get; set; }
    [MaxLength(16)] public string Split { get; set; } = "";
    [MaxLength(12)] public string DecisionLabel { get; set; } = "";
    public TimeOnly DecisionTimeEt { get; set; }
    public DateTime DecisionUtc { get; set; }
    /// <summary>Entry price: open of the first bar starting at or after t.</summary>
    public decimal? EntryRef { get; set; }
    public DateTime? EntryUtc { get; set; }
    public double? FwdMax0930 { get; set; }
    public double? FwdMax0945 { get; set; }
    public double? FwdMax1030 { get; set; }
    public double? FwdMax1600 { get; set; }
    public double? FwdClose0930 { get; set; }
    public double? FwdClose0945 { get; set; }
    public double? FwdClose1030 { get; set; }
    public double? FwdClose1600 { get; set; }
    public bool? Reach20By0945 { get; set; }
    public decimal? PmHigh { get; set; }
    public TimeOnly? HodTimePmEt { get; set; }
    public TimeOnly? HodTimeRthEt { get; set; }
    public TimeOnly? HodTimeEt { get; set; }
    [MaxLength(12)] public string? HodBucket { get; set; }
    public double? Fade { get; set; }
    public bool? FadeFlag30 { get; set; }
    public bool? CloseBelowOpen { get; set; }
    public double? ExtensionFromOpen0945 { get; set; }
    public TimeOnly? VelocityDeathTimeEt { get; set; }
    public int? DeathToHodMin { get; set; }
    public bool? HaltFlag { get; set; }
    public TimeOnly? VwapLossTimeEt { get; set; }
    public DateTime ComputedUtc { get; set; }
}

/// <summary>T4: one row per (filer CIK, filing) from EDGAR's quarterly <c>form.idx</c>, with the acceptance instant once fetched.</summary>
public sealed class EdgarFilingIndexRow
{
    public long Id { get; set; }
    [MaxLength(16)] public string Cik { get; set; } = "";
    [MaxLength(16)] public string? Symbol { get; set; }
    [MaxLength(16)] public string FormType { get; set; } = "";
    [MaxLength(24)] public string AccessionNumber { get; set; } = "";
    public DateOnly FiledDate { get; set; }
    public DateTime? AcceptedUtc { get; set; }
    [MaxLength(200)] public string CompanyName { get; set; } = "";
    /// <summary>"2024/QTR1".</summary>
    [MaxLength(12)] public string SourceQuarter { get; set; } = "";
    [MaxLength(200)] public string? ItemsJson { get; set; }
    public DateTime? HeaderFetchedUtc { get; set; }
}
