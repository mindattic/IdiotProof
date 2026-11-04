namespace IdiotProof.Evidence.Regimes;

/// <summary>How one detector did on a series with known regime switches.</summary>
public sealed record DetectorScore(string Method, int Switches, int Detected, double MeanDelay, int FalseAlarms, int Observations)
{
    public double FalseAlarmsPer1000 => Observations == 0 ? 0 : FalseAlarms * 1000.0 / Observations;
}

/// <summary>
/// Scores change detectors against planted switches: a switch counts as detected by the first event
/// at or after it within <c>maxDelay</c> observations (delay = event − switch); every other event is a
/// false alarm. Each detector only ever sees data up to the bar it flags.
/// </summary>
public static class RegimeDetectorComparison
{
    public static DetectorScore Score(string method, IReadOnlyList<ChangePoint> events, IReadOnlyList<int> switches, int observations, int maxDelay = 50)
    {
        var used = new bool[events.Count];
        var delays = new List<double>();
        foreach (var s in switches)
        {
            for (int e = 0; e < events.Count; e++)
            {
                if (used[e] || events[e].DetectedAt < s || events[e].DetectedAt - s > maxDelay) continue;
                used[e] = true;
                delays.Add(events[e].DetectedAt - s);
                break;
            }
        }
        int falseAlarms = used.Count(u => !u);
        return new DetectorScore(method, switches.Count, delays.Count, delays.Count == 0 ? double.NaN : delays.Average(), falseAlarms, observations);
    }

    /// <summary>
    /// Runs BOCPD, an HMM (trained on <paramref name="training"/>, a separate earlier sample) and the
    /// rolling variance-ratio classification over <paramref name="series"/>.
    /// </summary>
    public static IReadOnlyList<DetectorScore> Compare(IReadOnlyList<double> training, IReadOnlyList<double> series, IReadOnlyList<int> switches,
        int vrWindow = 60, int vrQ = 4, int maxDelay = 50)
    {
        var prior = NormalGammaPrior.FromSample(training);
        var bocpd = Bocpd.Detect(series, prior);
        var hmm = GaussianHmm.Fit(training).DetectSwitches(series);
        var vr = VarianceRatioSwitches(series, vrWindow, vrQ);
        return
        [
            Score("bocpd", bocpd, switches, series.Count, maxDelay),
            Score("hmm", hmm, switches, series.Count, maxDelay),
            Score("vr", vr, switches, series.Count, maxDelay),
        ];
    }

    /// <summary>Flags a switch whenever the trailing-window variance-ratio regime label changes.</summary>
    public static IReadOnlyList<ChangePoint> VarianceRatioSwitches(IReadOnlyList<double> returns, int window, int q)
    {
        var events = new List<ChangePoint>();
        Regime? last = null;
        for (int t = window; t <= returns.Count; t++)
        {
            var prices = new double[window + 1];
            prices[0] = 1;
            for (int i = 0; i < window; i++) prices[i + 1] = prices[i] * (1 + returns[t - window + i]);
            var label = RegimeStats.Read(prices, q).Regime;
            if (last is { } prev && prev != label) events.Add(new ChangePoint(t - 1, t - 1, 1));
            last = label;
        }
        return events;
    }
}
