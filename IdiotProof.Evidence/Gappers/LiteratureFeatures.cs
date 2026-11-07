using IdiotProof.Evidence.Economics;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// Lee–Mykland (2008) jump flag on one-minute log returns before t: the return over its local
/// bipower volatility, tested against the extreme-value critical value at <c>alpha</c>. Only real
/// bars count (forward-filled zero-volume minutes would deflate the bipower estimate). 1 when any
/// minute before t rejects, 0 otherwise, null with too few bars.
/// </summary>
public sealed class LeeMyklandJumpFeature(int localWindowBars = 60, double alpha = 0.01) : IGapperFeature
{
    public string Name => "jumpFlag";

    public double? Compute(GapperFeatureInputs inputs, GapperFormationSnapshot core)
    {
        var bars = inputs.MinuteBars.Where(b => b.EndUtc <= inputs.DecisionUtc && b.Volume > 0 && b.Close > 0).OrderBy(b => b.StartUtc).ToList();
        if (bars.Count < localWindowBars + 2) return null;
        var r = new double[bars.Count - 1];
        for (int i = 1; i < bars.Count; i++) r[i - 1] = Math.Log((double)bars[i].Close / (double)bars[i - 1].Close);
        int n = Math.Max(r.Length, 16 * 60);
        double c = Math.Sqrt(2.0 / Math.PI);
        double logN = Math.Log(n);
        double cn = Math.Sqrt(2 * logN) / c - (Math.Log(Math.PI) + Math.Log(logN)) / (2 * c * Math.Sqrt(2 * logN));
        double sn = 1.0 / (c * Math.Sqrt(2 * logN));
        double beta = -Math.Log(-Math.Log(1 - alpha));
        for (int i = localWindowBars; i < r.Length; i++)
        {
            double bipower = 0;
            for (int j = i - localWindowBars + 2; j < i; j++) bipower += Math.Abs(r[j]) * Math.Abs(r[j - 1]);
            var sigma = Math.Sqrt(bipower / (localWindowBars - 2));
            if (sigma <= 0) continue;
            var stat = (Math.Abs(r[i]) / sigma - cn) / sn;
            if (stat > beta) return 1;
        }
        return 0;
    }
}

/// <summary>
/// Corwin–Schultz spread over the prior days' extended-hours minute bars (D-1..D-5 as given in
/// <see cref="GapperFeatureInputs.PriorDayMinuteBars"/>), consecutive-bar pairs as the estimator
/// consumes them; null with fewer than <c>minBars</c> bars. The protocol's cost-model spread.
/// </summary>
public sealed class PriorDaysCorwinSchultzFeature(int minBars = 60) : IGapperFeature
{
    public const string FeatureName = "csSpreadPrior";

    public string Name => FeatureName;

    public double? Compute(GapperFeatureInputs inputs, GapperFormationSnapshot core)
    {
        var bars = inputs.PriorDayMinuteBars.SelectMany(d => d).Where(b => b.Volume > 0 && b.Low > 0).OrderBy(b => b.StartUtc).ToList();
        if (bars.Count < minBars) return null;
        var s = TradingCosts.CorwinSchultzSpread(bars);
        return double.IsNaN(s) ? null : s;
    }
}

/// <summary>
/// Post-registration exit-timing variant (IP-T442 note 10972): minutes from the premarket high to the
/// first minute with <c>consecutiveBars</c> consecutive negative v3 readings inside the
/// <c>windowMinutes</c> after that high, counted only when the run from the prior close to that high is
/// at least <c>minRunFraction</c>. Null when the run is too small or no death prints inside the window
/// by t. Uses bars ending at or before t only, so before 09:30 the "premarket high" is the high so far.
/// </summary>
public sealed class VelocityDeathNearPmHighFeature(double minRunFraction, int windowMinutes = 30, int velocityWindow = 3, int consecutiveBars = 2) : IGapperFeature
{
    public const string FeatureName = "velocityDeathNearPmHigh";

    public string Name => FeatureName;

    public double? Compute(GapperFeatureInputs inputs, GapperFormationSnapshot core)
    {
        var t = inputs.DecisionUtc;
        var sessionStart = MinuteBarMath.ToUtc(inputs.DateEt, MinuteBarMath.SessionStartEt);
        var pmCutoff = MinuteBarMath.ToUtc(inputs.DateEt, MinuteBarMath.RthOpenEt);
        var bars = MinuteBarMath.ForwardFill(inputs.MinuteBars, sessionStart, t);
        if (bars.Count == 0) return null;
        var priorClose = GapperFormationFeatures.PriorCloseOf(inputs);
        if (priorClose is not > 0) return null;

        Candle? pmHigh = null;
        foreach (var b in bars)
        {
            if (b.EndUtc > pmCutoff) break;
            if (pmHigh is null || b.High > pmHigh.High) pmHigh = b;
        }
        if (pmHigh is not { } high || (double)(high.High / priorClose.Value - 1m) < minRunFraction) return null;

        int negatives = 0;
        for (var m = high.EndUtc; m <= t && m <= high.EndUtc.AddMinutes(windowMinutes); m = m.AddMinutes(1))
        {
            var v = MinuteBarMath.Velocity(bars, m, velocityWindow);
            if (v is null) { negatives = 0; continue; }
            negatives = v < 0 ? negatives + 1 : 0;
            if (negatives >= consecutiveBars) return (m - high.StartUtc).TotalMinutes;
        }
        return null;
    }
}

/// <summary>A value computed outside the feature path (a rank among the day's universe, a share of promotional articles) carried into Extra.</summary>
public sealed class ConstantFeature(string name, double? value) : IGapperFeature
{
    public string Name => name;

    public double? Compute(GapperFeatureInputs inputs, GapperFormationSnapshot core) => value;
}
