namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// What a forming gapper looked like at one decision instant, computed only from data knowable then.
/// The fixed columns are the study's pre-registered features; <see cref="Extra"/> carries whatever
/// <see cref="IGapperFeature"/>s were registered. Null means "not computable from the inputs given".
/// </summary>
public sealed record GapperFormationSnapshot
{
    public required string Symbol { get; init; }
    public required DateOnly DateEt { get; init; }
    public required DateTime DecisionUtc { get; init; }

    /// <summary>Last close over the prior close, in percent.</summary>
    public double? GapPct { get; init; }

    /// <summary>Volume printed on D from 04:00 ET up to t.</summary>
    public double PmCumVol { get; init; }

    /// <summary>PmCumVol against what ADV and the time-of-day profile expect by t; null without a profile, decile and ADV.</summary>
    public double? VolRatioTod { get; init; }

    /// <summary>PmCumVol against the symbol's own prior days by the same clock time, shrunk n/(n+5) toward the profile expectation.</summary>
    public double? OwnProfileRatio { get; init; }

    public double? Velocity1m { get; init; }
    public double? Velocity3m { get; init; }
    public double? Velocity5m { get; init; }
    public double? Velocity15m { get; init; }

    /// <summary>v3 now minus v3 three minutes earlier.</summary>
    public double? Acceleration { get; init; }

    /// <summary>Volume of the last three minutes over the three before them.</summary>
    public double? VolumeRoc { get; init; }

    /// <summary>Trades in the last 15 minutes: the bars' trade counts when the feed carries them, else the number of bars with volume.</summary>
    public int TradesProxy { get; init; }

    /// <summary>Corwin-Schultz spread estimate over the last ten traded bars, as a fraction of price.</summary>
    public double? SpreadProxy { get; init; }

    /// <summary>How far the last close sits under the session high so far, in percent of that high.</summary>
    public double? DistToPmHighPct { get; init; }

    /// <summary>Minutes since the first minute whose volume reached the abnormal multiple of its profile expectation.</summary>
    public double? MinsSinceFirstAbnormal { get; init; }

    public double? SessionVwap { get; init; }

    public double? AbnormalReturnD1 { get; init; }
    public double? AbnormalReturnD2 { get; init; }
    public double? AbnormalReturnD3 { get; init; }
    public double? AbnormalReturnD4 { get; init; }
    public double? AbnormalReturnD5 { get; init; }
    public double? VolumeRatioD1 { get; init; }
    public double? VolumeRatioD2 { get; init; }
    public double? VolumeRatioD3 { get; init; }
    public double? VolumeRatioD4 { get; init; }
    public double? VolumeRatioD5 { get; init; }

    public int? DaysSinceReverseSplit { get; init; }
    public int? DaysSince424B { get; init; }
    public int? DaysSinceS1 { get; init; }

    public double? CatalystScore { get; init; }
    public double? CatalystAgeMinutes { get; init; }
    public int? CatalystDirection { get; init; }
    public string? CatalystPreClass { get; init; }

    /// <summary>True when the catalyst's timestamp is a batch ingestion stamp, so CatalystAgeMinutes is a lower bound on the real age.</summary>
    public bool? CatalystStampIsUpperBound { get; init; }

    public bool? Halted { get; init; }

    /// <summary>Registered literature add-ons plus the protocol features keyed by <see cref="GapperExtraKeys"/> (trade-count ROC, LLM sentiment, Form 4 purchases, 8-K items).</summary>
    public IReadOnlyDictionary<string, double> Extra { get; init; } = new Dictionary<string, double>();
}
