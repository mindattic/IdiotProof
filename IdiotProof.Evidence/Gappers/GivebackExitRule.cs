using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// Knobs of the volatility-scaled giveback (strategy-refined.md 2.5 overlay B, methods-exit.md rank 4): sell once
/// <c>ln(H / c) &gt; K x sigma1m x sqrt(HorizonMinutes)</c>, where H is the highest high since entry, c the last
/// close and sigma1m the standard deviation of 1-minute log returns over <see cref="SigmaWindowMinutes"/>.
/// <see cref="KAfterCollapse"/> (at most <see cref="K"/>) replaces K from the minute an exhaustion collapse is seen.
/// </summary>
public sealed record GivebackParameters(double K, int SigmaWindowMinutes = 15, int HorizonMinutes = 5, double? KAfterCollapse = null, double MinSigma = 0.001);

public sealed record GivebackVerdict(bool Exit, DateTime? SignalUtc, string Reason, double? GivebackLog, double? Threshold);

/// <summary>
/// "Sell once the price has given back more than noise allows": a trailing stop whose width scales with the recent
/// minute-return volatility, so a quiet name is held tighter than a wild one. Evaluated on closes at minute ends;
/// fails closed (hold) without enough minutes to estimate sigma. Pure.
/// </summary>
public sealed class GivebackExitRule
{
    private readonly GivebackParameters p;

    public GivebackExitRule(GivebackParameters parameters)
    {
        if (parameters.K <= 0) throw new ArgumentOutOfRangeException(nameof(parameters), "K must be positive.");
        if (parameters.KAfterCollapse is { } kc && (kc <= 0 || kc > parameters.K))
            throw new ArgumentOutOfRangeException(nameof(parameters), "The collapse K must be positive and can only tighten (<= K).");
        if (parameters.SigmaWindowMinutes < 3 || parameters.HorizonMinutes < 1)
            throw new ArgumentOutOfRangeException(nameof(parameters), "Sigma window must be at least three minutes and the horizon at least one.");
        p = parameters;
    }

    public GivebackParameters Parameters => p;

    /// <summary>
    /// Walks the minute ends after <paramref name="entryBarStartUtc"/> through <paramref name="untilUtc"/> and stops at
    /// the first one where the giveback from the running high exceeds the threshold. Live, call it with
    /// <c>untilUtc = now</c>.
    /// </summary>
    /// <param name="bars">The session's minute bars.</param>
    /// <param name="entryBarStartUtc">Start of the entry bar (the fill is that bar's open; its high counts toward H).</param>
    /// <param name="collapseUtc">The minute end at which an exhaustion collapse was seen, if any.</param>
    public GivebackVerdict Scan(IReadOnlyList<Candle> bars, DateTime entryBarStartUtc, DateTime untilUtc, DateTime? collapseUtc = null)
    {
        var filled = MinuteSeries.Filled(bars, entryBarStartUtc, untilUtc);
        var r = MinuteSeries.LogReturns(filled);
        decimal high = 0;
        double? lastGive = null, lastThreshold = null;
        for (int i = 0; i < filled.Count; i++)
        {
            var b = filled[i];
            if (b.StartUtc < entryBarStartUtc) continue;
            if (b.Volume > 0 && b.High > high) high = b.High;
            if (high <= 0 || b.Close <= 0) continue;
            // sigma from the returns up to and including this minute: the close being judged is already known.
            if (MinuteSeries.StdDev(r, i + 1, p.SigmaWindowMinutes, 5) is not { } sd) continue;
            double sigma = Math.Max(sd, p.MinSigma);
            double k = collapseUtc is { } c && b.EndUtc >= c && p.KAfterCollapse is { } kc ? kc : p.K;
            double threshold = k * sigma * Math.Sqrt(p.HorizonMinutes);
            double give = Math.Log((double)high / (double)b.Close);
            lastGive = give;
            lastThreshold = threshold;
            if (give > threshold)
                return new GivebackVerdict(true, b.EndUtc,
                    $"gave back {give * 100:0.##}% from the high (over {k:0.#} x {sigma * 100:0.###}%/min x sqrt {p.HorizonMinutes})", give, threshold);
        }
        return new GivebackVerdict(false, null, lastGive is null ? "too few minutes to estimate sigma" : "giveback within the volatility band", lastGive, lastThreshold);
    }
}
