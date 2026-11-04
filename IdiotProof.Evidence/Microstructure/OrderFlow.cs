using IdiotProof.Models;

namespace IdiotProof.Evidence.Microstructure;

/// <summary>Who did the trading inside one bar. Unsigned volume is from trades whose side couldn't be told.</summary>
public readonly record struct BarFlow(double BuyVolume, double SellVolume, double UnsignedVolume)
{
    public double SignedVolume => BuyVolume - SellVolume;

    /// <summary>(buy - sell) / (buy + sell) in [-1, 1]; NaN when no volume was signed.</summary>
    public double Imbalance => BuyVolume + SellVolume <= 0 ? double.NaN : (BuyVolume - SellVolume) / (BuyVolume + SellVolume);
}

/// <summary>Aggregates signed trades into per-bar buy/sell volume.</summary>
public static class OrderFlow
{
    /// <summary>
    /// Assigns each trade to the bar with StartUtc &lt;= t &lt; EndUtc. Trades and candles must be sorted by time.
    /// </summary>
    public static BarFlow[] PerBar(IReadOnlyList<Candle> candles, IReadOnlyList<TradePrint> trades, IReadOnlyList<int> signs)
    {
        if (signs.Count != trades.Count) throw new ArgumentException("One sign per trade.", nameof(signs));
        var buy = new double[candles.Count];
        var sell = new double[candles.Count];
        var unsigned = new double[candles.Count];
        int b = 0;
        for (int i = 0; i < trades.Count; i++)
        {
            var t = trades[i].TimeUtc;
            while (b < candles.Count && candles[b].EndUtc <= t) b++;
            if (b >= candles.Count) break;
            if (t < candles[b].StartUtc) continue;
            if (signs[i] > 0) buy[b] += trades[i].Size;
            else if (signs[i] < 0) sell[b] += trades[i].Size;
            else unsigned[b] += trades[i].Size;
        }
        var flows = new BarFlow[candles.Count];
        for (int i = 0; i < flows.Length; i++) flows[i] = new BarFlow(buy[i], sell[i], unsigned[i]);
        return flows;
    }

    /// <summary>
    /// Bulk volume classification (Easley, López de Prado, O'Hara): the buy share of bar i's volume is
    /// Φ(ΔP / σ), with σ the std of close-to-close changes over the trailing window ending at bar i.
    /// Needs no trade data, so it works from bars alone.
    /// </summary>
    public static BarFlow[] BulkClassify(IReadOnlyList<Candle> candles, int sigmaWindow = 50)
    {
        var flows = new BarFlow[candles.Count];
        var deltas = new double[candles.Count];
        for (int i = 1; i < candles.Count; i++) deltas[i] = (double)(candles[i].Close - candles[i - 1].Close);
        for (int i = 0; i < candles.Count; i++)
        {
            double v = (double)candles[i].Volume;
            if (i == 0) { flows[i] = new BarFlow(v / 2, v / 2, 0); continue; }
            int from = Math.Max(1, i - sigmaWindow + 1);
            var window = new double[i - from + 1];
            Array.Copy(deltas, from, window, 0, window.Length);
            double sigma = EvidenceStats.StdDev(window);
            double frac = sigma > 0 ? NormalCdf(deltas[i] / sigma) : deltas[i] > 0 ? 1 : deltas[i] < 0 ? 0 : 0.5;
            flows[i] = new BarFlow(v * frac, v * (1 - frac), 0);
        }
        return flows;
    }

    /// <summary>Standard normal CDF (Abramowitz-Stegun 7.1.26 erf, |error| &lt; 1.5e-7).</summary>
    public static double NormalCdf(double x)
    {
        double z = Math.Abs(x) / Math.Sqrt(2);
        double t = 1 / (1 + 0.3275911 * z);
        double erf = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-z * z);
        return x >= 0 ? 0.5 * (1 + erf) : 0.5 * (1 - erf);
    }
}
