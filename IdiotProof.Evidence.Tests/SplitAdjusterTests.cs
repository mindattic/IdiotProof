using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

public class SplitAdjusterTests
{
    private static Candle Day(DateOnly d, decimal close, decimal volume = 1000m) => new()
    {
        Symbol = "PNY", StartUtc = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), EndUtc = d.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        Open = close, High = close, Low = close, Close = close, Volume = volume,
    };

    private static DateOnly DateOf(Candle c) => DateOnly.FromDateTime(c.StartUtc);

    [Test]
    public void ReverseSplit_NoLongerReadsAsAHugeJump()
    {
        // 1-for-10 reverse split on Wednesday: raw closes 0.50 -> 5.10 is a +920% "day" that never happened.
        var bars = new[] { Day(new(2026, 7, 13), 0.52m, 5_000_000), Day(new(2026, 7, 14), 0.50m, 5_000_000), Day(new(2026, 7, 15), 5.10m, 500_000) };
        var split = new SplitEvent("PNY", new DateOnly(2026, 7, 15), OldRate: 10, NewRate: 1);

        var result = SplitAdjuster.Adjust(bars, new[] { split }, DateOf);

        Assert.That(result.SplitsApplied, Is.EqualTo(1));
        Assert.That(result.Bars[1].Close, Is.EqualTo(5.00m));
        Assert.That(result.Bars[2].Close, Is.EqualTo(5.10m), "bars on or after the ex-date keep their raw prices");
        double dailyReturn = (double)(result.Bars[2].Close / result.Bars[1].Close) - 1;
        Assert.That(dailyReturn, Is.EqualTo(0.02).Within(1e-9));
        Assert.That(result.Bars[0].Volume, Is.EqualTo(500_000m), "share volume is restated on the post-split share count");
    }

    [Test]
    public void ForwardSplit_HalvesPriorPrices_ForATwoForOne()
    {
        var bars = new[] { Day(new(2026, 7, 13), 100m), Day(new(2026, 7, 14), 51m) };
        var split = new SplitEvent("PNY", new DateOnly(2026, 7, 14), OldRate: 1, NewRate: 2);

        var result = SplitAdjuster.Adjust(bars, new[] { split }, DateOf);

        Assert.That(result.Bars[0].Close, Is.EqualTo(50m));
        Assert.That(result.Bars[0].Volume, Is.EqualTo(2000m));
    }

    [Test]
    public void SplitsOutsideTheWindow_AreIgnored_SoNoFutureEventLeaksIntoThePast()
    {
        var bars = new[] { Day(new(2026, 7, 13), 0.52m), Day(new(2026, 7, 14), 0.50m) };
        var later = new SplitEvent("PNY", new DateOnly(2026, 7, 20), 10, 1);
        var earlier = new SplitEvent("PNY", new DateOnly(2026, 7, 13), 10, 1);

        var result = SplitAdjuster.Adjust(bars, new[] { later, earlier }, DateOf);

        Assert.That(result.SplitsApplied, Is.Zero);
        Assert.That(result.Bars.Select(b => b.Close), Is.EqualTo(new[] { 0.52m, 0.50m }));
    }

    [Test]
    public void TwoSplitsInTheWindow_Compound()
    {
        var bars = new[] { Day(new(2026, 7, 13), 0.10m), Day(new(2026, 7, 14), 1.00m), Day(new(2026, 7, 15), 5.00m) };
        var splits = new[]
        {
            new SplitEvent("PNY", new DateOnly(2026, 7, 14), 10, 1),
            new SplitEvent("PNY", new DateOnly(2026, 7, 15), 5, 1),
        };

        var result = SplitAdjuster.Adjust(bars, splits, DateOf);

        Assert.That(result.SplitsApplied, Is.EqualTo(2));
        Assert.That(result.Bars.Select(b => b.Close), Is.EqualTo(new[] { 5.00m, 5.00m, 5.00m }));
        Assert.That(result.Bars.All(b => b.IsValid), Is.True);
    }

    [Test]
    public void NoSplits_ReturnsTheBarsUnchanged()
    {
        var bars = new[] { Day(new(2026, 7, 14), 2m), Day(new(2026, 7, 13), 1m) };
        var result = SplitAdjuster.Adjust(bars, Array.Empty<SplitEvent>(), DateOf);
        Assert.That(result.SplitsApplied, Is.Zero);
        Assert.That(result.Bars.Select(b => b.Close), Is.EqualTo(new[] { 1m, 2m }), "output is ordered oldest first");
    }
}
