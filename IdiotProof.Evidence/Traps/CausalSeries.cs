using IdiotProof.Models;

namespace IdiotProof.Evidence.Traps;

/// <summary>
/// Per-bar context every trap feature reads. Each array's value at bar i is computed from bars
/// 0..i only, so precomputing the whole series never leaks a future bar into a feature.
/// </summary>
public sealed class CausalSeries
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public IReadOnlyList<Candle> Candles { get; }
    /// <summary>Mean true range over the last <c>atrPeriod</c> bars (fewer at the start).</summary>
    public double[] Atr { get; }
    /// <summary>Session VWAP that resets at each ET calendar day.</summary>
    public double[] Vwap { get; }
    /// <summary>Mean volume of the up-to-20 bars before bar i (bar i excluded, so a spike isn't its own baseline).</summary>
    public double[] AvgVolume { get; }

    public CausalSeries(IReadOnlyList<Candle> candles, int atrPeriod = 14)
    {
        Candles = candles;
        int n = candles.Count;
        Atr = new double[n];
        Vwap = new double[n];
        AvgVolume = new double[n];

        var tr = new double[n];
        double pv = 0, vol = 0;
        DateOnly day = default;
        for (int i = 0; i < n; i++)
        {
            var c = candles[i];
            double hi = (double)c.High, lo = (double)c.Low, cl = (double)c.Close;
            tr[i] = i == 0 ? hi - lo : Math.Max(hi - lo, Math.Max(Math.Abs(hi - (double)candles[i - 1].Close), Math.Abs(lo - (double)candles[i - 1].Close)));
            int from = Math.Max(0, i - atrPeriod + 1);
            double sum = 0;
            for (int j = from; j <= i; j++) sum += tr[j];
            Atr[i] = sum / (i - from + 1);

            var etDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(c.StartUtc, DateTimeKind.Utc), Eastern));
            if (i == 0 || etDay != day) { pv = 0; vol = 0; day = etDay; }
            double typical = (hi + lo + cl) / 3;
            double v = (double)c.Volume;
            pv += typical * v;
            vol += v;
            Vwap[i] = vol > 0 ? pv / vol : typical;

            int vFrom = Math.Max(0, i - 20);
            double vs = 0;
            for (int j = vFrom; j < i; j++) vs += (double)candles[j].Volume;
            AvgVolume[i] = i == 0 ? v : vs / (i - vFrom);
        }
    }

    public double Close(int i) => (double)Candles[i].Close;
    public double High(int i) => (double)Candles[i].High;
    public double Low(int i) => (double)Candles[i].Low;
    public double Volume(int i) => (double)Candles[i].Volume;

    public double HighestHigh(int fromInclusive, int toInclusive)
    {
        double m = double.MinValue;
        for (int j = Math.Max(0, fromInclusive); j <= toInclusive; j++) m = Math.Max(m, High(j));
        return m;
    }

    public double LowestLow(int fromInclusive, int toInclusive)
    {
        double m = double.MaxValue;
        for (int j = Math.Max(0, fromInclusive); j <= toInclusive; j++) m = Math.Min(m, Low(j));
        return m;
    }
}
