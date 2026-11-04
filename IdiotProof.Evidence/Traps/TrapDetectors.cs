namespace IdiotProof.Evidence.Traps;

/// <summary>The four institutional-trap setups, each as a causal feature definition.</summary>
public static class TrapDetectors
{
    public const string FallingKnifeName = "FallingKnife";
    public const string FailedBreakoutName = "FailedBreakout";
    public const string StopHuntWickName = "StopHuntWick";
    public const string VwapLossName = "VwapLoss";

    /// <summary>
    /// Catching a falling knife: features are [consecutive lower lows (capped at 8), down-bar volume
    /// acceleration (mean volume of the last 5 down bars over the 20-bar average), distance below VWAP
    /// in ATRs, return over the last <paramref name="lookback"/> bars]. Present whenever price is lower
    /// than it was <paramref name="lookback"/> bars ago.
    /// </summary>
    public static TrapDetector FallingKnife(int lookback = 10, int horizon = 10, double adverseAtr = 1.0, int bins = 3)
        => new(FallingKnifeName, Math.Max(lookback, 20), (s, i) =>
        {
            double ret = s.Close(i) / s.Close(i - lookback) - 1;
            if (ret >= 0) return null;
            return new[] { LowerLows(s, i), DownVolumeAcceleration(s, i), BelowVwapAtr(s, i), ret };
        }, horizon, adverseAtr, bins);

    /// <summary>
    /// Bull trap: some bar in the last <paramref name="withinBars"/> broke above the prior
    /// <paramref name="lookback"/>-bar high and the current bar closed back below it. Features are
    /// [bars since the breakout, close below the broken high in ATRs, breakout-bar volume ratio].
    /// </summary>
    public static TrapDetector FailedBreakout(int lookback = 20, int withinBars = 3, int horizon = 10, double adverseAtr = 1.0, int bins = 3)
        => new(FailedBreakoutName, lookback + withinBars, (s, i) =>
        {
            for (int j = i; j > i - withinBars && j > lookback; j--)
            {
                double priorHigh = s.HighestHigh(j - lookback, j - 1);
                if (s.High(j) <= priorHigh) continue;
                if (s.Close(i) >= priorHigh) return null;
                double atr = Math.Max(s.Atr[i], 1e-9);
                return new[] { (double)(i - j), (priorHigh - s.Close(i)) / atr, Ratio(s.Volume(j), s.AvgVolume[j]) };
            }
            return null;
        }, horizon, adverseAtr, bins);

    /// <summary>
    /// Stop-hunt wick: the bar's low pierced the prior <paramref name="lookback"/>-bar low (running the
    /// stops under it) and the bar closed back above that low. Features are [wick depth below the
    /// prior low in ATRs, close position within the bar's range (1 = closed at the high), volume ratio].
    /// </summary>
    public static TrapDetector StopHuntWick(int lookback = 20, int horizon = 10, double adverseAtr = 1.0, int bins = 3)
        => new(StopHuntWickName, lookback, (s, i) =>
        {
            double priorLow = s.LowestLow(i - lookback, i - 1);
            if (s.Low(i) >= priorLow || s.Close(i) <= priorLow) return null;
            double atr = Math.Max(s.Atr[i], 1e-9);
            double range = s.High(i) - s.Low(i);
            double closePos = range <= 0 ? 0.5 : (s.Close(i) - s.Low(i)) / range;
            return new[] { (priorLow - s.Low(i)) / atr, closePos, Ratio(s.Volume(i), s.AvgVolume[i]) };
        }, horizon, adverseAtr, bins);

    /// <summary>
    /// VWAP loss: the bar closed below session VWAP after the prior bar closed at or above it.
    /// Features are [distance below VWAP in ATRs, return over the last 10 bars, volume ratio].
    /// </summary>
    public static TrapDetector VwapLoss(int horizon = 10, double adverseAtr = 1.0, int bins = 3)
        => new(VwapLossName, 20, (s, i) =>
        {
            if (!(s.Close(i) < s.Vwap[i] && s.Close(i - 1) >= s.Vwap[i - 1])) return null;
            return new[] { BelowVwapAtr(s, i), s.Close(i) / s.Close(i - 10) - 1, Ratio(s.Volume(i), s.AvgVolume[i]) };
        }, horizon, adverseAtr, bins);

    public static IReadOnlyList<TrapDetector> All() => new[] { FallingKnife(), FailedBreakout(), StopHuntWick(), VwapLoss() };

    /// <summary>How many bars in a row, ending at bar i, each made a lower low than the bar before (capped at 8).</summary>
    public static double LowerLows(CausalSeries s, int i)
    {
        int count = 0;
        for (int j = i; j > 0 && count < 8 && s.Low(j) < s.Low(j - 1); j--) count++;
        return count;
    }

    /// <summary>Mean volume of the last 5 down bars (close below prior close) within 20 bars, over the 20-bar average.</summary>
    public static double DownVolumeAcceleration(CausalSeries s, int i)
    {
        double sum = 0;
        int found = 0;
        for (int j = i; j > 0 && j > i - 20 && found < 5; j--)
            if (s.Close(j) < s.Close(j - 1)) { sum += s.Volume(j); found++; }
        return found == 0 ? 0 : Ratio(sum / found, s.AvgVolume[i]);
    }

    /// <summary>(VWAP - close) / ATR: positive when price sits below VWAP.</summary>
    public static double BelowVwapAtr(CausalSeries s, int i) => (s.Vwap[i] - s.Close(i)) / Math.Max(s.Atr[i], 1e-9);

    private static double Ratio(double v, double avg) => avg > 0 ? v / avg : 1;
}
