using IdiotProof.Models;

namespace IdiotProof.Evidence.Adaptation;

/// <summary>How much of the period must have printed before anything is generated or adapted.</summary>
public sealed record InformationGateOptions
{
    /// <summary>Closed bars required since the period started.</summary>
    public int MinBars { get; init; } = 15;

    /// <summary>Shares traded in those closed bars.</summary>
    public decimal MinVolume { get; init; }

    /// <summary>Wall-clock time that must have passed since the period started.</summary>
    public TimeSpan MinElapsed { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// When set, a Bayesian online change-point run (BOCPD) over the period's closed-bar returns resets
    /// the evidence: bars before the latest detected regime change stop counting, and adaptation waits
    /// for <see cref="MinBarsAfterChange"/> fresh bars. Null leaves change detection off.
    /// </summary>
    public double? ChangePointThreshold { get; init; }

    /// <summary>Closed bars required after a detected regime change before adapting again.</summary>
    public int MinBarsAfterChange { get; init; } = 10;

    /// <summary>BOCPD prior probability that any given bar starts a new regime.</summary>
    public double ChangePointHazard { get; init; } = 1.0 / 250;

    /// <summary>A change counts when the regime is this likely to have begun within the last this-many bars.</summary>
    public int ChangePointWindow { get; init; } = 5;
}

/// <param name="EvidenceStartUtc">
/// Start of the bars adaptation may learn from: the first bar of the latest detected regime, or null
/// when no change was detected (the whole period counts).
/// </param>
public readonly record struct GateDecision(bool Ready, int ClosedBars, decimal Volume, string Reason, DateTime? EvidenceStartUtc = null);

/// <summary>
/// "Wait for enough information": the first thin minutes of a period act on data that hasn't
/// locked in yet, so nothing adapts until enough closed bars, volume and time have accumulated.
/// Only bars that had fully closed by <c>now</c> count; a still-forming bar is never evidence.
/// </summary>
public static class InformationGate
{
    public static GateDecision Check(IReadOnlyList<Candle> candles, DateTime periodStartUtc, DateTime nowUtc,
        InformationGateOptions? options = null)
    {
        options ??= new InformationGateOptions();
        int bars = 0;
        decimal volume = 0;
        foreach (var c in candles)
        {
            if (c.StartUtc < periodStartUtc || c.EndUtc > nowUtc) continue;
            bars++;
            volume += c.Volume;
        }

        var elapsed = nowUtc - periodStartUtc;
        if (elapsed < options.MinElapsed)
            return new(false, bars, volume, $"only {elapsed.TotalMinutes:0} of {options.MinElapsed.TotalMinutes:0} minutes into the period");
        if (bars < options.MinBars)
            return new(false, bars, volume, $"only {bars} of {options.MinBars} closed bars");
        if (volume < options.MinVolume)
            return new(false, bars, volume, $"only {volume:N0} of {options.MinVolume:N0} shares traded");

        if (options.ChangePointThreshold is { } threshold)
        {
            var periodBars = ClosedBars(candles.Where(c => c.StartUtc >= periodStartUtc), nowUtc);
            var change = LatestChange(periodBars, threshold, options);
            if (change is { } startIndex)
            {
                int fresh = periodBars.Count - startIndex;
                var startUtc = periodBars[startIndex].StartUtc;
                if (fresh < options.MinBarsAfterChange)
                    return new(false, bars, volume,
                        $"regime changed at bar {startIndex + 1}; only {fresh} of {options.MinBarsAfterChange} bars since", startUtc);
                return new(true, bars, volume, $"enough information since the regime change at bar {startIndex + 1}", startUtc);
            }
        }
        return new(true, bars, volume, "enough information");
    }

    /// <summary>
    /// Index (into <paramref name="closedBars"/>) of the first bar of the most recent detected regime,
    /// or null if BOCPD found no change. Returns are close-to-close, so return i belongs to bar i+1.
    /// </summary>
    public static int? LatestChange(IReadOnlyList<Candle> closedBars, double threshold, InformationGateOptions options)
    {
        if (closedBars.Count < 3) return null;
        var returns = new List<double>(closedBars.Count - 1);
        for (int i = 1; i < closedBars.Count; i++)
        {
            double prev = (double)closedBars[i - 1].Close;
            returns.Add(prev > 0 ? Math.Log((double)closedBars[i].Close / prev) : 0);
        }
        int warmup = Math.Max(20, 2 * options.ChangePointWindow);
        if (returns.Count <= warmup) return null;
        double tickFloor = Regimes.TickNoise.ReturnVarianceFloor(closedBars.Select(c => c.Close).ToList());
        var prior = Regimes.NormalGammaPrior.FromSample(returns.Take(warmup).ToList(), tickFloor);
        var events = Regimes.Bocpd.Detect(returns, prior, options.ChangePointHazard, options.ChangePointWindow, threshold, warmup, minVariance: tickFloor);
        if (events.Count == 0) return null;
        return Math.Clamp(events[^1].StartsAt + 1, 0, closedBars.Count - 1);
    }

    /// <summary>The bars that had fully closed by <paramref name="nowUtc"/>, oldest first: the only bars adaptation may read.</summary>
    public static List<Candle> ClosedBars(IEnumerable<Candle> candles, DateTime nowUtc) =>
        candles.Where(c => c.EndUtc <= nowUtc).OrderBy(c => c.StartUtc).ToList();
}
