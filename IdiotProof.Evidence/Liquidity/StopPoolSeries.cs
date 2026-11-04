using IdiotProof.Models;

namespace IdiotProof.Evidence.Liquidity;

/// <summary>Where resting stops are likely to cluster.</summary>
public enum PoolKind
{
    RoundNumber, PriorDayHigh, PriorDayLow, PremarketHigh, PremarketLow,
    OpeningRangeHigh, OpeningRangeLow, VwapBand,
}

/// <summary>Above the price (buy stops of shorts) or below it (sell stops of longs).</summary>
public enum PoolSide { Above, Below }

public readonly record struct StopPool(PoolKind Kind, double Price);

public sealed record StopPoolOptions
{
    /// <summary>Minutes after 9:30 ET that make up the opening range.</summary>
    public int OpeningRangeMinutes { get; init; } = 15;
    /// <summary>VWAP band multiples of the session's volume-weighted σ of typical price.</summary>
    public IReadOnlyList<double> VwapBands { get; init; } = [1.0, 2.0];
}

/// <summary>
/// The stop-pool levels known after each bar closes. Every level at bar i is computed from bars
/// 0..i only: the premarket range exists only once the regular session has started, the opening
/// range only once its last minute has closed, and the prior day's range only once a new ET day begins.
/// </summary>
public sealed class StopPoolSeries
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeSpan RthOpen = new(9, 30, 0);
    private static readonly TimeSpan RthClose = new(16, 0, 0);

    private readonly double[] priorHigh, priorLow, pmHigh, pmLow, orHigh, orLow, vwap, vwapSigma, atr;
    private readonly DateOnly[] day;
    private readonly StopPoolOptions options;

    public IReadOnlyList<Candle> Candles { get; }

    public StopPoolSeries(IReadOnlyList<Candle> candles, StopPoolOptions? options = null, int atrPeriod = 14)
    {
        Candles = candles;
        this.options = options ?? new StopPoolOptions();
        int n = candles.Count;
        priorHigh = Fill(n); priorLow = Fill(n); pmHigh = Fill(n); pmLow = Fill(n);
        orHigh = Fill(n); orLow = Fill(n); vwap = Fill(n); vwapSigma = Fill(n);
        atr = new double[n];
        day = new DateOnly[n];

        var orEnd = RthOpen.Add(TimeSpan.FromMinutes(this.options.OpeningRangeMinutes));
        DateOnly current = default;
        double dayHi = double.NaN, dayLo = double.NaN, rthHi = double.NaN, rthLo = double.NaN;
        double prevHi = double.NaN, prevLo = double.NaN;
        double pmHi = double.NaN, pmLo = double.NaN, orHi = double.NaN, orLo = double.NaN;
        double sv = 0, svp = 0, svp2 = 0;
        var tr = new double[n];

        for (int i = 0; i < n; i++)
        {
            var c = candles[i];
            double hi = (double)c.High, lo = (double)c.Low, cl = (double)c.Close;
            var startEt = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(c.StartUtc), Eastern);
            var endTod = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(c.EndUtc), Eastern).TimeOfDay;
            var d = DateOnly.FromDateTime(startEt);
            day[i] = d;

            if (i == 0 || d != current)
            {
                if (i > 0)
                {
                    prevHi = double.IsNaN(rthHi) ? dayHi : rthHi;
                    prevLo = double.IsNaN(rthLo) ? dayLo : rthLo;
                }
                current = d;
                dayHi = dayLo = rthHi = rthLo = pmHi = pmLo = orHi = orLo = double.NaN;
                sv = svp = svp2 = 0;
            }

            var tod = startEt.TimeOfDay;
            dayHi = Max(dayHi, hi); dayLo = Min(dayLo, lo);
            if (tod < RthOpen) { pmHi = Max(pmHi, hi); pmLo = Min(pmLo, lo); }
            else if (tod < RthClose) { rthHi = Max(rthHi, hi); rthLo = Min(rthLo, lo); }
            if (tod >= RthOpen && tod < orEnd) { orHi = Max(orHi, hi); orLo = Min(orLo, lo); }

            priorHigh[i] = prevHi; priorLow[i] = prevLo;
            if (tod >= RthOpen) { pmHigh[i] = pmHi; pmLow[i] = pmLo; }
            if (endTod >= orEnd && tod >= RthOpen) { orHigh[i] = orHi; orLow[i] = orLo; }

            double tp = (hi + lo + cl) / 3, v = Math.Max(0, (double)c.Volume);
            sv += v; svp += v * tp; svp2 += v * tp * tp;
            if (sv > 0)
            {
                double m = svp / sv;
                vwap[i] = m;
                vwapSigma[i] = Math.Sqrt(Math.Max(0, svp2 / sv - m * m));
            }

            tr[i] = i == 0 ? hi - lo : Math.Max(hi - lo, Math.Max(Math.Abs(hi - (double)candles[i - 1].Close), Math.Abs(lo - (double)candles[i - 1].Close)));
            int from = Math.Max(0, i - atrPeriod + 1);
            double sum = 0;
            for (int j = from; j <= i; j++) sum += tr[j];
            atr[i] = sum / (i - from + 1);
        }
    }

    public DateOnly Day(int i) => day[i];
    public double Atr(int i) => atr[i];
    public double Close(int i) => (double)Candles[i].Close;
    public double High(int i) => (double)Candles[i].High;
    public double Low(int i) => (double)Candles[i].Low;

    /// <summary>Every pool known after bar i closed.</summary>
    public IReadOnlyList<StopPool> PoolsAt(int i)
    {
        var pools = new List<StopPool>();
        double close = Close(i);
        double inc = RoundIncrement(close);
        if (inc > 0)
        {
            double below = Math.Floor(close / inc + 1e-9) * inc;
            if (below >= close - 1e-9) below -= inc;
            double above = Math.Ceiling(close / inc - 1e-9) * inc;
            if (above <= close + 1e-9) above += inc;
            if (below > 0) pools.Add(new StopPool(PoolKind.RoundNumber, Math.Round(below, 4)));
            pools.Add(new StopPool(PoolKind.RoundNumber, Math.Round(above, 4)));
        }
        Add(pools, PoolKind.PriorDayHigh, priorHigh[i]);
        Add(pools, PoolKind.PriorDayLow, priorLow[i]);
        Add(pools, PoolKind.PremarketHigh, pmHigh[i]);
        Add(pools, PoolKind.PremarketLow, pmLow[i]);
        Add(pools, PoolKind.OpeningRangeHigh, orHigh[i]);
        Add(pools, PoolKind.OpeningRangeLow, orLow[i]);
        // A σ that is only rounding noise (flat typical price) would put phantom bands on the price itself.
        if (!double.IsNaN(vwap[i]) && vwapSigma[i] > 1e-6 * vwap[i])
            foreach (var k in options.VwapBands)
            {
                Add(pools, PoolKind.VwapBand, vwap[i] + k * vwapSigma[i]);
                Add(pools, PoolKind.VwapBand, vwap[i] - k * vwapSigma[i]);
            }
        return pools;
    }

    /// <summary>Round-number spacing by price tier: $0.05 under $1, $0.10 under $5, $0.25 under $20, $0.50 under $100, else $1.</summary>
    public static double RoundIncrement(double price) => price switch
    {
        <= 0 => 0,
        < 1 => 0.05,
        < 5 => 0.10,
        < 20 => 0.25,
        < 100 => 0.50,
        _ => 1.00,
    };

    /// <summary>Price tier used to key learned rates: 0 under $1, 1 under $5, 2 under $20, 3 at $20+.</summary>
    public static int Tier(double price) => price < 1 ? 0 : price < 5 ? 1 : price < 20 ? 2 : 3;

    private static void Add(List<StopPool> pools, PoolKind kind, double price)
    {
        if (!double.IsNaN(price) && price > 0) pools.Add(new StopPool(kind, price));
    }

    private static double[] Fill(int n)
    {
        var a = new double[n];
        Array.Fill(a, double.NaN);
        return a;
    }

    private static double Max(double a, double b) => double.IsNaN(a) ? b : Math.Max(a, b);
    private static double Min(double a, double b) => double.IsNaN(a) ? b : Math.Min(a, b);
    private static DateTime AsUtc(DateTime t) => t.Kind == DateTimeKind.Utc ? t : DateTime.SpecifyKind(t, DateTimeKind.Utc);
}
