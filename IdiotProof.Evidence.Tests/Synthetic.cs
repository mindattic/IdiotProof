using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

internal static class Synthetic
{
    public static double[] RandomWalkReturns(int n, int seed, double sigma = 0.01)
    {
        var rng = new Random(seed);
        var r = new double[n];
        for (int i = 0; i < n; i++) r[i] = sigma * Gaussian(rng);
        return r;
    }

    /// <summary>AR(1) returns: phi &gt; 0 trends, phi &lt; 0 mean-reverts.</summary>
    public static double[] Ar1Returns(int n, double phi, int seed, double sigma = 0.01)
    {
        var rng = new Random(seed);
        var r = new double[n];
        for (int i = 1; i < n; i++) r[i] = phi * r[i - 1] + sigma * Gaussian(rng);
        return r;
    }

    public static double[] PricesFromReturns(IReadOnlyList<double> returns, double start = 3.0)
    {
        var p = new double[returns.Count + 1];
        p[0] = start;
        for (int i = 0; i < returns.Count; i++) p[i + 1] = p[i] * (1 + returns[i]);
        return p;
    }

    public static List<Candle> Candles(IReadOnlyList<double> closes, string symbol = "TEST")
    {
        var t0 = new DateTime(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);
        var list = new List<Candle>(closes.Count);
        for (int i = 0; i < closes.Count; i++)
        {
            decimal c = (decimal)closes[i];
            decimal o = i == 0 ? c : (decimal)closes[i - 1];
            list.Add(new Candle
            {
                Symbol = symbol,
                StartUtc = t0.AddMinutes(i),
                EndUtc = t0.AddMinutes(i + 1),
                Open = o,
                Close = c,
                High = Math.Max(o, c),
                Low = Math.Min(o, c),
                Volume = 1000,
            });
        }
        return list;
    }

    public static double Gaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
