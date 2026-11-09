using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// Knobs of the drift-change exit (methods-exit.md 1.3, strategy-refined.md 2.5 overlay A).
/// <see cref="PosteriorThreshold"/> is p*; <see cref="PostHighDrift"/> is mu1, the mean 1-minute log return after
/// the high on train gapper mornings (one number per fit window); mu2 is the mean 1-minute log return over the
/// <see cref="RunDriftWindowMinutes"/> before arming; sigma is the rolling standard deviation of 1-minute log
/// returns over <see cref="SigmaWindowMinutes"/>. <see cref="CollapseHazardMultiplier"/> scales the hazard from
/// the minute an imbalance/trade-count collapse is seen; it is at least 1, so the collapse can only raise it.
/// </summary>
public sealed record DriftChangeParameters(
    double PosteriorThreshold,
    double PostHighDrift,
    int SigmaWindowMinutes = 15,
    int RunDriftWindowMinutes = 15,
    double CollapseHazardMultiplier = 1,
    double MinSigma = 0.001);

/// <summary>What the detector says at the end of a scan: whether (and at which minute end) the posterior crossed p*.</summary>
public sealed record DriftChangeVerdict(bool Exit, DateTime? SignalUtc, string Reason, double? Posterior, double? ShiryaevRoberts, double? RunDrift, double? Sigma);

/// <summary>
/// "Sell when the drift has probably switched": Shiryaev's quickest-detection posterior on 1-minute log returns
/// (Ekström and Lindberg 2013 show selling the first time it crosses a constant level is optimal for a drift that
/// drops from mu2 to mu1 at an unobserved time). Each minute: the prior moves by the time-of-day hazard of the high
/// (<see cref="HodHazard"/>, times the collapse multiplier once a collapse is seen), then the likelihood ratio
/// <c>L = exp(((mu1 - mu2) r - (mu1^2 - mu2^2) / 2) / sigma^2)</c> updates it. The Shiryaev-Roberts statistic
/// <c>R = (1 + R) L</c> is reported beside it. Fails closed: too few minutes before arming, or a run drift not
/// above the post-high drift, means hold (the clock and the guards still apply). It can only sell earlier than
/// the clock; it never extends a hold. Pure.
/// </summary>
public sealed class DriftChangeExitRule
{
    private readonly DriftChangeParameters p;
    private readonly HodHazard hazard;

    public DriftChangeExitRule(DriftChangeParameters parameters, HodHazard hazard)
    {
        if (parameters.PosteriorThreshold is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(parameters), "The posterior threshold must be in (0, 1).");
        if (parameters.CollapseHazardMultiplier < 1)
            throw new ArgumentOutOfRangeException(nameof(parameters), "The collapse multiplier can only raise the hazard (>= 1).");
        if (parameters.SigmaWindowMinutes < 3 || parameters.RunDriftWindowMinutes < 3)
            throw new ArgumentOutOfRangeException(nameof(parameters), "Windows must be at least three minutes.");
        p = parameters;
        this.hazard = hazard;
    }

    public DriftChangeParameters Parameters => p;

    public HodHazard Hazard => hazard;

    /// <summary>
    /// Runs the detector from <paramref name="armUtc"/> (the entry) through <paramref name="untilUtc"/> and stops at
    /// the first minute end where the posterior reaches p*. Live, call it with <c>untilUtc = now</c>: once it has
    /// fired it stays fired.
    /// </summary>
    /// <param name="bars">The session's minute bars (only bars ending by <paramref name="untilUtc"/> are read).</param>
    /// <param name="collapseUtc">The minute end at which an imbalance or trade-count collapse was seen, if any.</param>
    public DriftChangeVerdict Scan(IReadOnlyList<Candle> bars, DateTime armUtc, DateTime untilUtc, DateTime? collapseUtc = null)
    {
        var filled = MinuteSeries.Filled(bars, armUtc, untilUtc);
        var r = MinuteSeries.LogReturns(filled);
        int arm = filled.FindIndex(b => b.EndUtc > armUtc);
        if (arm < 0) return Hold("no minute after arming", null, null);
        var mu2 = MinuteSeries.Mean(r, arm, p.RunDriftWindowMinutes, Math.Max(3, p.RunDriftWindowMinutes / 2));
        if (mu2 is not { } run) return Hold($"fewer than {Math.Max(3, p.RunDriftWindowMinutes / 2)} minutes before arming", null, null);
        double mu1 = p.PostHighDrift;
        if (run <= mu1) return Hold($"run drift {run:0.#####}/min is not above the post-high drift {mu1:0.#####}/min", run, null);

        double post = 0, sr = 0;
        double? lastSigma = null;
        for (int i = arm; i < filled.Count; i++)
        {
            if (!double.IsFinite(r[i])) continue;
            if (MinuteSeries.StdDev(r, i, p.SigmaWindowMinutes, 5) is not { } sd) continue;
            double sigma = Math.Max(sd, p.MinSigma);
            lastSigma = sigma;
            double lambda = hazard.AtUtc(filled[i].StartUtc);
            if (collapseUtc is { } c && filled[i].StartUtc >= c) lambda *= p.CollapseHazardMultiplier;
            lambda = Math.Min(1, lambda);
            double logL = Math.Clamp(((mu1 - run) * r[i] - (mu1 * mu1 - run * run) / 2) / (sigma * sigma), -50, 50);
            double l = Math.Exp(logL);
            double prior = post + (1 - post) * lambda;
            post = prior * l / (prior * l + (1 - prior));
            sr = Math.Min((1 + sr) * l, 1e300);
            if (post >= p.PosteriorThreshold)
                return new DriftChangeVerdict(true, filled[i].EndUtc,
                    $"drift-change posterior {post:0.000} reached {p.PosteriorThreshold:0.00} (run {run * 100:0.###}%/min, sigma {sigma * 100:0.###}%/min)", post, sr, run, sigma);
        }
        return new DriftChangeVerdict(false, null, $"posterior {post:0.000} below {p.PosteriorThreshold:0.00}", post, sr, run, lastSigma);
    }

    /// <summary>The run drift mu2 and the sigma at arming (the snr = mu2 / sigma diagnostic); null parts when too few minutes.</summary>
    public static (double? RunDrift, double? Sigma) Arming(IReadOnlyList<Candle> bars, DateTime armUtc, int runWindowMinutes = 15, int sigmaWindowMinutes = 15)
    {
        var filled = MinuteSeries.Filled(bars, armUtc, armUtc);
        var r = MinuteSeries.LogReturns(filled);
        int end = filled.Count;
        return (MinuteSeries.Mean(r, end, runWindowMinutes, Math.Max(3, runWindowMinutes / 2)), MinuteSeries.StdDev(r, end, sigmaWindowMinutes, 5));
    }

    /// <summary>
    /// Mean 1-minute log return over the <paramref name="minutes"/> after the high-of-day bar starting at
    /// <paramref name="hodStartUtc"/> (mu1 for one train gapper morning); null with fewer than half the minutes.
    /// </summary>
    public static double? PostHighDrift(IReadOnlyList<Candle> bars, DateTime hodStartUtc, int minutes = 30)
    {
        var filled = MinuteSeries.Filled(bars, hodStartUtc, hodStartUtc.AddMinutes(minutes + 1));
        var r = MinuteSeries.LogReturns(filled);
        int hod = filled.FindIndex(b => b.StartUtc >= hodStartUtc);
        if (hod < 0) return null;
        var after = new List<double>();
        for (int i = hod + 1; i < filled.Count && i <= hod + minutes; i++)
            if (double.IsFinite(r[i])) after.Add(r[i]);
        return after.Count >= minutes / 2 ? after.Average() : null;
    }

    private static DriftChangeVerdict Hold(string reason, double? run, double? sigma) => new(false, null, reason, null, null, run, sigma);
}

/// <summary>
/// The exhaustion state (H17; strategy-refined.md 2.5): not an exit by itself, a warning that raises the drift-change
/// hazard and tightens the giveback from that minute. Triggered by the bar trade count falling (tradesRoc at or below
/// the threshold, every morning has it) or, where trade flow exists, the 5-minute retail imbalance collapse. Trade
/// flow was pulled for gapper mornings only, so in a backtest it is outcome-selected; use the bar trigger there.
/// </summary>
public static class ExhaustionState
{
    public const double DefaultTradesRocThreshold = -0.5;

    /// <summary>The first minute end in (fromUtc, untilUtc] where the trade-count rate of change is at or below <paramref name="threshold"/>.</summary>
    public static DateTime? TradesCollapse(IReadOnlyList<Candle> bars, DateTime fromUtc, DateTime untilUtc, int windowMinutes = 5, double threshold = DefaultTradesRocThreshold)
    {
        var t = new DateTime(fromUtc.Ticks - fromUtc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc).AddMinutes(1);
        for (; t <= untilUtc; t = t.AddMinutes(1))
            if (GapperFormationFeatures.TradesRoc(bars, t, windowMinutes) is { } roc && roc <= threshold) return t;
        return null;
    }

    /// <summary>The earliest of the given collapse times (null when none).</summary>
    public static DateTime? Earliest(params DateTime?[] times)
    {
        DateTime? best = null;
        foreach (var t in times)
            if (t is { } v && (best is null || v < best)) best = v;
        return best;
    }
}

/// <summary>Clock-minute series shared by the exit overlays: forward-filled from 04:00 ET, log returns, rolling moments.</summary>
internal static class MinuteSeries
{
    /// <summary>One bar per clock minute from the session's 04:00 ET to <paramref name="untilUtc"/> (bars ending by it).</summary>
    public static List<Candle> Filled(IReadOnlyList<Candle> bars, DateTime anchorUtc, DateTime untilUtc) =>
        MinuteBarMath.ForwardFill(bars, MinuteBarMath.ToUtc(MinuteBarMath.SessionDate(anchorUtc), MinuteBarMath.SessionStartEt), untilUtc);

    /// <summary>r[i] = ln(close_i / close_(i-1)); r[0] is NaN.</summary>
    public static double[] LogReturns(List<Candle> filled)
    {
        var r = new double[filled.Count];
        if (r.Length > 0) r[0] = double.NaN;
        for (int i = 1; i < filled.Count; i++)
            r[i] = filled[i].Close > 0 && filled[i - 1].Close > 0 ? Math.Log((double)filled[i].Close / (double)filled[i - 1].Close) : double.NaN;
        return r;
    }

    /// <summary>Mean of the finite returns in [end - window, end); null with fewer than <paramref name="minCount"/>.</summary>
    public static double? Mean(double[] r, int end, int window, int minCount)
    {
        double sum = 0;
        int n = 0;
        for (int i = Math.Max(0, end - window); i < end; i++)
            if (double.IsFinite(r[i])) { sum += r[i]; n++; }
        return n >= minCount ? sum / n : null;
    }

    /// <summary>Sample standard deviation of the finite returns in [end - window, end); null with fewer than <paramref name="minCount"/>.</summary>
    public static double? StdDev(double[] r, int end, int window, int minCount)
    {
        double sum = 0, sq = 0;
        int n = 0;
        for (int i = Math.Max(0, end - window); i < end; i++)
            if (double.IsFinite(r[i])) { sum += r[i]; sq += r[i] * r[i]; n++; }
        if (n < Math.Max(2, minCount)) return null;
        double mean = sum / n;
        return Math.Sqrt(Math.Max(0, (sq - n * mean * mean) / (n - 1)));
    }
}
