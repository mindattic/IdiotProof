namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// The onset and volume-confirmed-gap features (IP-T470, methods-onset.md P1 and P4), registered after
/// the freeze through <see cref="GapperFeatureRegistry.AddOnset"/>. Each reads <see cref="OnsetTrack"/>
/// over bars ending at or before t, so it never looks ahead.
/// </summary>
public static class OnsetFeatureNames
{
    /// <summary>Volume CUSUM at t (k = ln 2).</summary>
    public const string CusumVol = "cusumVol";
    /// <summary>Trade-count CUSUM at t (k = ln 2).</summary>
    public const string CusumTrades = "cusumTrades";
    /// <summary>Minutes since either CUSUM first reached the provisional threshold; missing when it has not.</summary>
    public const string MinsSinceCusumAlarm = "minsSinceCusumAlarm";
    /// <summary>Premarket VWAP over the prior close, minus 1.</summary>
    public const string PmVwapRet = "pmVwapRet";
    /// <summary>pmVwapRet / pmRet, guarded.</summary>
    public const string Conf = "conf";
    /// <summary>Share of premarket dollar volume traded above prior close x 1.10.</summary>
    public const string DollarShareAbove10 = "pmDollarShareAbove10";

    public static readonly string[] All = [CusumVol, CusumTrades, MinsSinceCusumAlarm, PmVwapRet, Conf, DollarShareAbove10];
}

/// <summary>One onset feature: a value read off the last minute of the series at t.</summary>
public sealed class OnsetFeature(string name, TimeOfDayProfile? tradeProfile, double threshold = CountCusum.ProvisionalThreshold) : IGapperFeature
{
    public string Name => name;

    public double? Compute(GapperFeatureInputs inputs, GapperFormationSnapshot core)
    {
        var series = OnsetTrack.Compute(inputs, tradeProfile);
        int last = series.Minutes - 1;
        if (last < 0) return null;
        double? At(double[]? v) => v is null || double.IsNaN(v[last]) ? null : v[last];
        switch (name)
        {
            case OnsetFeatureNames.CusumVol: return At(series.CusumVolume(CountCusum.DefaultDrift));
            case OnsetFeatureNames.CusumTrades: return At(series.CusumTrades(CountCusum.DefaultDrift));
            case OnsetFeatureNames.PmVwapRet: return At(series.PmVwapRet);
            case OnsetFeatureNames.Conf: return At(series.Conf);
            case OnsetFeatureNames.DollarShareAbove10: return At(series.DollarShareAbove10);
            case OnsetFeatureNames.MinsSinceCusumAlarm:
                int first = -1;
                foreach (var path in new[] { series.CusumVolume(CountCusum.DefaultDrift), series.CusumTrades(CountCusum.DefaultDrift) })
                    if (path is not null && CountCusum.FirstAlarm(path, threshold) is var a && a >= 0 && (first < 0 || a < first)) first = a;
                return first < 0 ? null : last - first;
            default: throw new InvalidOperationException($"Unknown onset feature '{name}'.");
        }
    }
}
