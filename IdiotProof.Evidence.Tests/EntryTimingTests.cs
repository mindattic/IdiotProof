using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T414: labels fill at the next bar's open by default, so bid-ask bounce can't pose as an edge.</summary>
public class EntryTimingTests
{
    /// <summary>
    /// A ~$1 stock whose fair value takes a tiny random walk; each bar opens at the fair value and
    /// closes at a random side of a 1-cent spread (bid or ask). Nothing about the next fair-value
    /// move is predictable.
    /// </summary>
    private static List<Candle> BounceSeries(int bars, int seed)
    {
        var rng = new Random(seed);
        var t0 = new DateTime(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);
        var list = new List<Candle>();
        double mid = 1.0;
        for (int i = 0; i < bars; i++)
        {
            double open = mid;
            mid += 0.001 * Synthetic.Gaussian(rng);
            double close = mid + (rng.Next(2) == 0 ? -0.005 : 0.005);
            list.Add(new Candle
            {
                Symbol = "BNC", StartUtc = t0.AddMinutes(i), EndUtc = t0.AddMinutes(i + 1),
                Open = (decimal)open, Close = (decimal)close,
                High = (decimal)Math.Max(open, close), Low = (decimal)Math.Min(open, close), Volume = 1000,
            });
        }
        return list;
    }

    [Test]
    public void CloseEntry_ManufacturesReversalEdge_FromBidAskBounce_NextOpenDoesNot()
    {
        var bars = BounceSeries(2000, seed: 5);
        var closeCard = new DirectionalScorecard();
        var openCard = new DirectionalScorecard();
        for (int i = 1; i + 1 < bars.Count; i++)
        {
            // "Reversal" rule: buy the close that printed below the open, sell the one above it.
            var call = bars[i].Close < bars[i].Open ? Direction.Up : Direction.Down;
            closeCard.Record(call, ForwardReturnLabeler.ForwardReturn(bars, i, 1, EntryTiming.Close));
            openCard.Record(call, ForwardReturnLabeler.ForwardReturn(bars, i, 1, EntryTiming.NextOpen));
        }

        Assert.That(closeCard.HitRate, Is.GreaterThan(0.65), "close entry scores the bounce as an edge: " + closeCard);
        Assert.That(closeCard.IsSignificant(0.001), Is.True);
        Assert.That(openCard.HitRate, Is.EqualTo(0.5).Within(0.04), "filled at the next open there is no edge: " + openCard);
        Assert.That(openCard.PValue, Is.GreaterThan(0.01));
    }

    [Test]
    public void DefaultLabel_FillsAtTheNextOpen()
    {
        var bars = Synthetic.Candles(new[] { 1.00, 1.10, 1.20 });
        // Give bar 1 an open that differs from bar 0's close.
        bars[1] = new Candle { Symbol = "X", StartUtc = bars[1].StartUtc, EndUtc = bars[1].EndUtc, Open = 1.05m, High = 1.10m, Low = 1.05m, Close = 1.10m, Volume = 1000 };

        var nextOpen = ForwardReturnLabeler.Label(bars, horizon: 1);
        var atClose = ForwardReturnLabeler.Label(bars, horizon: 1, entryTiming: EntryTiming.Close);

        Assert.That(nextOpen[0].ForwardReturn, Is.EqualTo(1.10 / 1.05 - 1).Within(1e-12));
        Assert.That(atClose[0].ForwardReturn, Is.EqualTo(0.10).Within(1e-12));
    }

    [Test]
    public void ForwardReturn_IsNaN_WhenTheExitBarDoesNotExist()
    {
        var bars = Synthetic.Candles(new[] { 1.0, 1.1, 1.2 });
        Assert.That(ForwardReturnLabeler.ForwardReturn(bars, 1, 2), Is.NaN);
        Assert.That(ForwardReturnLabeler.ForwardReturn(bars, 0, 2), Is.EqualTo(0.2).Within(1e-12));
    }

    [Test]
    public void AnalogPredictor_DefaultsToNextOpen_AndCanBeAskedForClose()
    {
        Assert.That(new PatternAnalogPredictor(5, 1).Entry, Is.EqualTo(EntryTiming.NextOpen));
        Assert.That(new PatternAnalogPredictor(5, 1, entry: EntryTiming.Close).Entry, Is.EqualTo(EntryTiming.Close));
    }
}
