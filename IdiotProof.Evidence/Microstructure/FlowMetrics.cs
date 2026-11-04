using IdiotProof.Models;

namespace IdiotProof.Evidence.Microstructure;

/// <summary>
/// Order-flow measurements per bar. Every value at bar i reads bars 0..i only, so it can be
/// tested against what happened after bar i without leaking the future.
/// </summary>
public static class FlowMetrics
{
    /// <summary>
    /// VPIN: volume is poured into equal buckets of <paramref name="bucketVolume"/> shares; each full
    /// bucket's toxicity is |buy - sell| / bucketVolume, and bar i's VPIN is the mean over the last
    /// <paramref name="window"/> buckets completed by the end of bar i (NaN until that many exist).
    /// Unsigned volume is split evenly. Always in [0, 1]; near 1 when the flow is one-sided.
    /// </summary>
    public static double[] Vpin(IReadOnlyList<BarFlow> flows, double bucketVolume, int window = 50)
    {
        if (bucketVolume <= 0) throw new ArgumentOutOfRangeException(nameof(bucketVolume));
        if (window < 1) throw new ArgumentOutOfRangeException(nameof(window));
        var result = new double[flows.Count];
        var toxicity = new List<double>();
        double fillBuy = 0, fillSell = 0;
        for (int i = 0; i < flows.Count; i++)
        {
            double buy = flows[i].BuyVolume + flows[i].UnsignedVolume / 2;
            double sell = flows[i].SellVolume + flows[i].UnsignedVolume / 2;
            double total = buy + sell;
            double buyShare = total > 0 ? buy / total : 0.5;
            while (total > 0)
            {
                double room = bucketVolume - fillBuy - fillSell;
                double take = Math.Min(room, total);
                fillBuy += take * buyShare;
                fillSell += take * (1 - buyShare);
                total -= take;
                if (fillBuy + fillSell >= bucketVolume - 1e-9)
                {
                    toxicity.Add(Math.Abs(fillBuy - fillSell) / bucketVolume);
                    fillBuy = fillSell = 0;
                }
            }
            if (toxicity.Count < window) { result[i] = double.NaN; continue; }
            double sum = 0;
            for (int k = toxicity.Count - window; k < toxicity.Count; k++) sum += toxicity[k];
            result[i] = sum / window;
        }
        return result;
    }

    /// <summary>OLS slope of price change on signed volume: Kyle's lambda, the price impact per share of net order flow.</summary>
    public static double KylesLambda(IReadOnlyList<double> priceChanges, IReadOnlyList<double> signedVolumes)
    {
        int n = Math.Min(priceChanges.Count, signedVolumes.Count);
        if (n < 3) return double.NaN;
        double mx = 0, my = 0;
        for (int i = 0; i < n; i++) { mx += signedVolumes[i]; my += priceChanges[i]; }
        mx /= n; my /= n;
        double sxy = 0, sxx = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = signedVolumes[i] - mx;
            sxy += dx * (priceChanges[i] - my);
            sxx += dx * dx;
        }
        return sxx <= 0 ? double.NaN : sxy / sxx;
    }

    /// <summary>Kyle's lambda over the trailing <paramref name="window"/> bars ending at each bar (NaN before enough bars).</summary>
    public static double[] RollingKylesLambda(IReadOnlyList<Candle> candles, IReadOnlyList<BarFlow> flows, int window = 30)
    {
        var result = new double[candles.Count];
        var dp = new double[window];
        var q = new double[window];
        for (int i = 0; i < candles.Count; i++)
        {
            if (i < window) { result[i] = double.NaN; continue; }
            for (int k = 0; k < window; k++)
            {
                int j = i - window + 1 + k;
                dp[k] = (double)(candles[j].Close - candles[j - 1].Close);
                q[k] = flows[j].SignedVolume;
            }
            result[i] = KylesLambda(dp, q);
        }
        return result;
    }

    /// <summary>
    /// Absorption: heavy one-sided volume that fails to move price. +1 = sellers dominated the last
    /// <paramref name="window"/> bars (sell share ≥ <paramref name="minOpposingShare"/>) on above-normal volume,
    /// yet price fell less than <paramref name="maxProgressAtr"/> ATRs: someone is soaking up the selling.
    /// -1 is the mirror (buying absorbed). 0 otherwise, including when history is too short.
    /// </summary>
    public static int[] Absorption(IReadOnlyList<Candle> candles, IReadOnlyList<BarFlow> flows, int window = 5,
        double minOpposingShare = 0.65, double maxProgressAtr = 0.5, double minRelativeVolume = 1.5, int atrWindow = 14)
    {
        int baseline = window * 3;
        var result = new int[candles.Count];
        for (int i = 0; i < candles.Count; i++)
        {
            if (i < window + baseline || i < atrWindow) continue;
            double buy = 0, sell = 0, vol = 0;
            for (int j = i - window + 1; j <= i; j++)
            {
                buy += flows[j].BuyVolume;
                sell += flows[j].SellVolume;
                vol += (double)candles[j].Volume;
            }
            double prior = 0;
            for (int j = i - window - baseline + 1; j <= i - window; j++) prior += (double)candles[j].Volume;
            double relVol = prior <= 0 ? double.PositiveInfinity : (vol / window) / (prior / baseline);
            if (relVol < minRelativeVolume || buy + sell <= 0) continue;

            double atr = 0;
            for (int j = i - atrWindow + 1; j <= i; j++)
            {
                double hi = (double)candles[j].High, lo = (double)candles[j].Low, pc = (double)candles[j - 1].Close;
                atr += Math.Max(hi - lo, Math.Max(Math.Abs(hi - pc), Math.Abs(lo - pc)));
            }
            atr /= atrWindow;
            if (atr <= 0) continue;

            double progress = (double)(candles[i].Close - candles[i - window].Close);
            double sellShare = sell / (buy + sell);
            if (sellShare >= minOpposingShare && progress > -maxProgressAtr * atr) result[i] = 1;
            else if (1 - sellShare >= minOpposingShare && progress < maxProgressAtr * atr) result[i] = -1;
        }
        return result;
    }
}
