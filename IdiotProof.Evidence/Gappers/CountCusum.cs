namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// One-sided count CUSUM against a time-varying expectation (methods-onset.md P1, strategy-refined.md
/// §2.2a, IP-T470). For minute m with observed count <c>v_m</c> (shares or prints) and expectation
/// <c>e_m</c> from the time-of-day profile:
/// <code>
/// z_m = min( ln((v_m + c) / (e_m + c)), cap )      log excess, 0 = normal; the cap stops one block print alarming alone
/// S_m = max(0, S_{m-1} + z_m - k)                   k = drift, ln 2 detects a sustained doubling
/// alarm = first m with S_m >= h
/// </code>
/// Page (1954); optimal for a given false-alarm budget (Lorden 1971, Moustakides 1986). Pure: the
/// caller passes the series, so live and backtest share it. <c>h</c> is set on train control
/// mornings only (the explore <c>alarms</c> track calibrates it per fold).
/// </summary>
public static class CountCusum
{
    /// <summary>k = ln 2: tuned to detect a sustained doubling over the expectation.</summary>
    public static readonly double DefaultDrift = Math.Log(2);

    /// <summary>z is capped at ln 20 per minute.</summary>
    public static readonly double DefaultCap = Math.Log(20);

    /// <summary>Volume pseudocount c = ADV20 x this (keeps the 04:00 minutes finite).</summary>
    public const double VolumePseudocountFraction = 1e-5;

    /// <summary>
    /// Trade-count pseudocount, in prints. One print: a lone print in a minute that normally has none
    /// scores ln 2, exactly the drift, so it adds nothing; two or more prints accumulate.
    /// </summary>
    public const double TradePseudocount = 1;

    /// <summary>
    /// Provisional threshold for the registry feature <c>minsSinceCusumAlarm</c> only. It is NOT a
    /// calibrated value: the explore <c>alarms</c> track sets h on train control mornings per fold.
    /// </summary>
    public const double ProvisionalThreshold = 5;

    /// <summary>The capped log excess of one minute.</summary>
    public static double Excess(double observed, double expected, double pseudocount, double cap)
    {
        var z = Math.Log((Math.Max(0, observed) + pseudocount) / (Math.Max(0, expected) + pseudocount));
        return Math.Min(z, cap);
    }

    /// <summary>One CUSUM step: S_m from S_{m-1}.</summary>
    public static double Step(double previous, double observed, double expected, double pseudocount, double drift, double cap) =>
        Math.Max(0, previous + Excess(observed, expected, pseudocount, cap) - drift);

    /// <summary>The statistic after every minute: <c>result[i]</c> is S after minute i.</summary>
    public static double[] Path(IReadOnlyList<double> observed, IReadOnlyList<double> expected, double pseudocount, double drift, double cap)
    {
        if (observed.Count != expected.Count) throw new ArgumentException("Observed and expected series must have the same length.");
        if (pseudocount <= 0) throw new ArgumentOutOfRangeException(nameof(pseudocount), "The pseudocount must be positive.");
        var s = new double[observed.Count];
        double prev = 0;
        for (int i = 0; i < s.Length; i++) s[i] = prev = Step(prev, observed[i], expected[i], pseudocount, drift, cap);
        return s;
    }

    /// <summary>First index in [from, to] whose statistic reaches the threshold; -1 when none.</summary>
    public static int FirstAlarm(IReadOnlyList<double> path, double threshold, int from = 0, int to = int.MaxValue)
    {
        int last = Math.Min(to, path.Count - 1);
        for (int i = Math.Max(0, from); i <= last; i++)
            if (path[i] >= threshold) return i;
        return -1;
    }

    /// <summary>The first alarm minute for threshold h and drift k over a whole series; -1 when none.</summary>
    public static int FirstAlarm(IReadOnlyList<double> observed, IReadOnlyList<double> expected, double pseudocount, double drift, double threshold, double? cap = null) =>
        FirstAlarm(Path(observed, expected, pseudocount, drift, cap ?? DefaultCap), threshold);
}
