using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// A news item or filing already classified into a catalyst, as known at decision time.
/// <paramref name="StampIsUpperBound"/> is true when <paramref name="PublishedUtc"/> is a batch
/// ingestion stamp (fiscal.ai wire copies land on half-hour boundaries), so it bounds the real
/// publication time from above and is never exact. <paramref name="LlmSentiment"/> is +1/-1/0 when
/// an LLM classified the item, null when only the deterministic pre-classifier ran.
/// </summary>
public sealed record CatalystObservation(DateTime PublishedUtc, string PreClass, int Direction, int SourceTier, double Score, bool StampIsUpperBound = false, int? LlmSentiment = null);

/// <summary>An EDGAR filing with its acceptance instant and 8-K item codes (empty for other forms).</summary>
public sealed record FilingObservation(string FormType, DateTime AcceptedUtc, IReadOnlyList<string> Items);

/// <summary>One Form 4 filing: when EDGAR accepted it, how many open-market purchase lines (code P) it carries and their dollar total.</summary>
public sealed record Form4FilingObservation(DateTime AcceptedUtc, int PurchaseLines, double PurchaseDollars);

/// <summary>Dates of the corporate actions the study tracks; null when none is known.</summary>
public sealed record CorporateActionDates(DateOnly? ReverseSplit = null, DateOnly? Offering424B = null, DateOnly? S1 = null);

/// <summary>
/// Everything <see cref="GapperFormationFeatures.Compute"/> may look at for one symbol-day at one
/// decision instant. The compute trims every list to what was knowable at <see cref="DecisionUtc"/>
/// itself, so callers may pass whole days.
/// </summary>
public sealed record GapperFeatureInputs
{
    public required string Symbol { get; init; }

    /// <summary>The ET trading date D being evaluated.</summary>
    public required DateOnly DateEt { get; init; }

    /// <summary>Decision instant t; nothing ending after it is read.</summary>
    public required DateTime DecisionUtc { get; init; }

    /// <summary>Extended-hours one-minute bars of D from 04:00 ET (more is fine; trimmed to End &lt;= t).</summary>
    public required IReadOnlyList<Candle> MinuteBars { get; init; }

    /// <summary>Daily bars dated before D, ascending.</summary>
    public IReadOnlyList<Candle> DailyBars { get; init; } = [];

    /// <summary>Prior session's official close; derived from the last daily bar when null.</summary>
    public decimal? PriorClose { get; init; }

    public TimeOfDayProfile? Profile { get; init; }

    /// <summary>ADV decile used to pick the profile row; required for the profile ratios.</summary>
    public int? AdvDecile { get; init; }

    /// <summary>Average daily share volume over the 20 sessions before D; derived from daily bars when null.</summary>
    public double? Adv20Shares { get; init; }

    /// <summary>
    /// Average daily trade count over the 20 sessions before D (the count CUSUM's baseline, IP-T470);
    /// derived from daily bars that carry a trade count, else from the prior days' minute bars, when null.
    /// </summary>
    public double? Adt20Trades { get; init; }

    /// <summary>The trade-count time-of-day profile (<see cref="TimeOfDayProfile.BuildTradeCount"/>); the count CUSUM is null without it.</summary>
    public TimeOfDayProfile? TradeProfile { get; init; }

    /// <summary>The symbol's own minute bars for D-1..D-5 (any subset, one list per day).</summary>
    public IReadOnlyList<IReadOnlyList<Candle>> PriorDayMinuteBars { get; init; } = [];

    public IReadOnlyList<CatalystObservation> Catalysts { get; init; } = [];

    public IReadOnlyList<FilingObservation> Filings { get; init; } = [];

    /// <summary>Form 4 filings of the symbol's CIK; null when the symbol has no CIK mapping (the feature is then null, not zero).</summary>
    public IReadOnlyList<Form4FilingObservation>? Form4Filings { get; init; }

    public CorporateActionDates? CorporateActions { get; init; }

    public bool? Halted { get; init; }

    /// <summary>A minute is "abnormal" when its volume is at least this many times the profile's expectation for that clock minute.</summary>
    public double AbnormalVolumeMultiple { get; init; } = 5;

    /// <summary>The catalyst block looks back this many hours from t for the latest 8-K item codes.</summary>
    public int CatalystLookbackHours { get; init; } = 24;

    /// <summary>Form 4 purchases are counted over [D - this many days, t].</summary>
    public int Form4LookbackDays { get; init; } = 30;

    /// <summary>Window of the trade-count rate of change: trades over the last k minutes against the k before them.</summary>
    public int TradesRocWindowMinutes { get; init; } = 5;

    /// <summary>Trade-count proxy window: trades summed over the last this many minutes.</summary>
    public int TradesProxyWindowMinutes { get; init; } = 15;
}
