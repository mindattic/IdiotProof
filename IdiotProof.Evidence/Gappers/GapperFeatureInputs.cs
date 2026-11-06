using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>A news item or filing already classified into a catalyst, as known at decision time.</summary>
public sealed record CatalystObservation(DateTime PublishedUtc, string PreClass, int Direction, int SourceTier, double Score);

/// <summary>An EDGAR filing with its acceptance instant and 8-K item codes (empty for other forms).</summary>
public sealed record FilingObservation(string FormType, DateTime AcceptedUtc, IReadOnlyList<string> Items);

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

    /// <summary>The symbol's own minute bars for D-1..D-5 (any subset, one list per day).</summary>
    public IReadOnlyList<IReadOnlyList<Candle>> PriorDayMinuteBars { get; init; } = [];

    public IReadOnlyList<CatalystObservation> Catalysts { get; init; } = [];

    public IReadOnlyList<FilingObservation> Filings { get; init; } = [];

    public CorporateActionDates? CorporateActions { get; init; }

    public bool? Halted { get; init; }

    /// <summary>A minute is "abnormal" when its volume is at least this many times the profile's expectation for that clock minute.</summary>
    public double AbnormalVolumeMultiple { get; init; } = 5;
}
