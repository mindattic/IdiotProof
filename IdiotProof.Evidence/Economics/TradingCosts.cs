using IdiotProof.Models;

namespace IdiotProof.Evidence.Economics;

/// <summary>Knobs for the cost model. Defaults are commission-free equities with a square-root impact constant of 1.</summary>
public sealed record CostParameters
{
    /// <summary>Square-root-law constant Y in impact = Y·σ·sqrt(Q/ADV); empirical studies put it near 0.5–1.</summary>
    public double ImpactConstant { get; init; } = 1.0;
    public double FeePerShare { get; init; }
    public double FeePerOrder { get; init; }
    /// <summary>Spread used when neither quotes nor a bar estimate are available.</summary>
    public double FallbackSpread { get; init; } = 0.003;
}

/// <summary>A round trip's estimated cost, all as fractions of price.</summary>
public sealed record CostEstimate(double Spread, double ImpactPerSide, double FeesRoundTrip, string SpreadSource)
{
    /// <summary>Full spread (half paid on each side) + impact on entry and exit + fees.</summary>
    public double RoundTrip => Spread + 2 * ImpactPerSide + FeesRoundTrip;
}

/// <summary>
/// What a round trip really costs: the bid-ask spread (from quotes, or estimated from bar highs and
/// lows by Corwin-Schultz when quotes are missing), square-root market impact in the Almgren-Chriss /
/// Torre tradition, and fees.
/// </summary>
public static class TradingCosts
{
    /// <summary>Mean quoted spread as a fraction of the midpoint.</summary>
    public static double QuotedSpread(IEnumerable<(double Bid, double Ask)> quotes)
    {
        double sum = 0;
        int n = 0;
        foreach (var (bid, ask) in quotes)
        {
            if (bid <= 0 || ask < bid) continue;
            sum += (ask - bid) / ((ask + bid) / 2);
            n++;
        }
        return n == 0 ? double.NaN : sum / n;
    }

    /// <summary>
    /// Corwin &amp; Schultz (2012) high-low spread estimator averaged over consecutive bar pairs:
    /// the high-low range of one bar mixes volatility and spread, the range of two bars mixes twice
    /// the volatility and the same spread, so the difference isolates the spread. Negative pair
    /// estimates are set to zero, as in the paper.
    /// </summary>
    public static double CorwinSchultzSpread(IReadOnlyList<Candle> bars)
    {
        double k = 3 - 2 * Math.Sqrt(2);
        double sum = 0;
        int n = 0;
        for (int i = 1; i < bars.Count; i++)
        {
            double h1 = (double)bars[i - 1].High, l1 = (double)bars[i - 1].Low;
            double h2 = (double)bars[i].High, l2 = (double)bars[i].Low;
            if (l1 <= 0 || l2 <= 0) continue;
            double beta = Square(Math.Log(h1 / l1)) + Square(Math.Log(h2 / l2));
            double gamma = Square(Math.Log(Math.Max(h1, h2) / Math.Min(l1, l2)));
            double alpha = (Math.Sqrt(2 * beta) - Math.Sqrt(beta)) / k - Math.Sqrt(gamma / k);
            double s = 2 * (Math.Exp(alpha) - 1) / (1 + Math.Exp(alpha));
            sum += Math.Max(0, s);
            n++;
        }
        return n == 0 ? double.NaN : sum / n;
    }

    /// <summary>Square-root impact per side: Y·σ_daily·sqrt(Q/ADV).</summary>
    public static double SquareRootImpact(double shares, double averageDailyVolume, double dailyVolatility, double impactConstant = 1.0)
    {
        if (shares <= 0 || averageDailyVolume <= 0 || dailyVolatility <= 0) return 0;
        return impactConstant * dailyVolatility * Math.Sqrt(shares / averageDailyVolume);
    }

    /// <summary>
    /// Estimates a round trip from bars that are already in the past (never the day being traded):
    /// spread by Corwin-Schultz unless quotes are given, daily volatility from per-bar log returns
    /// scaled by bars per session, ADV from total volume over the sessions.
    /// </summary>
    public static CostEstimate Estimate(IReadOnlyList<Candle> priorBars, int sessions, double shares,
        CostParameters? parameters = null, IEnumerable<(double Bid, double Ask)>? quotes = null)
    {
        var p = parameters ?? new CostParameters();
        double spread = quotes is null ? double.NaN : QuotedSpread(quotes);
        string source = "quotes";
        if (double.IsNaN(spread)) { spread = CorwinSchultzSpread(priorBars); source = "corwin-schultz"; }
        if (double.IsNaN(spread)) { spread = p.FallbackSpread; source = "fallback"; }

        double impact = 0, fees = 0;
        if (priorBars.Count > 1 && sessions > 0)
        {
            var logReturns = new List<double>(priorBars.Count - 1);
            double volume = 0;
            for (int i = 0; i < priorBars.Count; i++)
            {
                volume += (double)priorBars[i].Volume;
                if (i > 0 && priorBars[i - 1].Close > 0 && priorBars[i].Close > 0)
                    logReturns.Add(Math.Log((double)priorBars[i].Close / (double)priorBars[i - 1].Close));
            }
            double barsPerSession = (double)priorBars.Count / sessions;
            double dailyVol = EvidenceStats.StdDev(logReturns) * Math.Sqrt(barsPerSession);
            impact = SquareRootImpact(shares, volume / sessions, dailyVol, p.ImpactConstant);

            double price = (double)priorBars[^1].Close;
            if (price > 0 && shares > 0)
                fees = 2 * (p.FeePerShare * shares + p.FeePerOrder) / (shares * price);
        }
        return new CostEstimate(spread, impact, fees, source);
    }

    private static double Square(double x) => x * x;
}
