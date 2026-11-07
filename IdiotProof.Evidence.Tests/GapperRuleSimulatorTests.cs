using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T442 A2: the H12 rule replay (entry, each exit, the halt fill) and the frozen cost model. 2026-07-17 is EDT: 08:30 ET = 12:30 UTC.</summary>
public class GapperRuleSimulatorTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static DateTime Et(int h, int m) => MinuteBarMath.ToUtc(Day, new TimeSpan(h, m, 0));

    private static Candle Bar(int h, int m, double open, double close, double? low = null, double? high = null, double volume = 100) => new()
    {
        Symbol = "T", StartUtc = Et(h, m), EndUtc = Et(h, m).AddMinutes(1),
        Open = (decimal)open, High = (decimal)(high ?? Math.Max(open, close)), Low = (decimal)(low ?? Math.Min(open, close)), Close = (decimal)close, Volume = (decimal)volume,
    };

    private static GapperRuleConfig Config(double stop = 10, bool vwap = false, string sellBy = "09:28") =>
        new(3, 0.5, 3, stop, TimeSpan.Parse(sellBy), vwap);

    /// <summary>Flat 10.00 bars from 08:00 to 08:29, then whatever the test appends from 08:30.</summary>
    private static List<Candle> Premarket()
    {
        var bars = new List<Candle>();
        for (int m = 0; m < 30; m++) bars.Add(Bar(8, m, 10, 10));
        return bars;
    }

    [Test]
    public void Entry_IsTheOpenOfTheFirstBarAtOrAfterThePass_AndSellByFillsAtTheSellByOpen()
    {
        var bars = Premarket();
        for (int m = 30; m < 60; m++) bars.Add(Bar(8, m, 10 + 0.01 * (m - 30), 10 + 0.01 * (m - 29)));
        for (int m = 0; m < 30; m++) bars.Add(Bar(9, m, 10.3, 10.3));
        var trade = GapperRuleSimulator.Simulate(Config(stop: 50), bars, Day, Et(8, 30), 0.005)!;
        Assert.Multiple(() =>
        {
            Assert.That(trade.EntryUtc, Is.EqualTo(Et(8, 30)));
            Assert.That(trade.Entry, Is.EqualTo(10.00m), "the 08:30 bar's open, not its close");
            Assert.That(trade.ExitReason, Is.EqualTo("sell-by"));
            Assert.That(trade.ExitUtc, Is.EqualTo(Et(9, 28)));
            Assert.That(trade.Exit, Is.EqualTo(10.3m));
            Assert.That(trade.GrossReturn, Is.EqualTo(0.03).Within(1e-9));
            Assert.That(trade.NetReturn, Is.EqualTo(0.025).Within(1e-9));
            Assert.That(trade.HaltFill, Is.False);
        });
    }

    [Test]
    public void Stop_FillsAtTheStopPrice_OrTheOpenWhenGappedThrough()
    {
        var bars = Premarket();
        bars.Add(Bar(8, 30, 10, 10));
        bars.Add(Bar(8, 31, 10, 9.3, low: 9.2));
        var atStop = GapperRuleSimulator.Simulate(Config(stop: 5), bars, Day, Et(8, 30), 0)!;
        var gapped = Premarket();
        gapped.Add(Bar(8, 30, 10, 10));
        gapped.Add(Bar(8, 31, 9.0, 9.1, low: 8.9));
        var atOpen = GapperRuleSimulator.Simulate(Config(stop: 5), gapped, Day, Et(8, 30), 0)!;
        Assert.Multiple(() =>
        {
            Assert.That(atStop.ExitReason, Is.EqualTo("stop"));
            Assert.That(atStop.Exit, Is.EqualTo(9.5m));
            Assert.That(atOpen.Exit, Is.EqualTo(9.0m), "a bar that opens under the stop fills at its open");
        });
    }

    [Test]
    public void VelocityDeath_ExitsAtTheNextBarOpen_AfterTwoNegativeReadingsFollowingAnArmedRun()
    {
        var bars = Premarket();
        double[] closes = [10.1, 10.3, 10.6, 10.9, 11.1, 11.0, 10.9, 10.8, 10.7, 10.6];
        double prev = 10;
        for (int i = 0; i < closes.Length; i++) { bars.Add(Bar(8, 30 + i, prev, closes[i])); prev = closes[i]; }
        var trade = GapperRuleSimulator.Simulate(Config(stop: 50), bars, Day, Et(8, 30), 0)!;
        Assert.Multiple(() =>
        {
            Assert.That(trade.ExitReason, Is.EqualTo("velocity-death"));
            Assert.That(trade.ExitUtc, Is.EqualTo(Et(8, 39)), "v3 is exactly zero at the 08:37 read (resets the count), negative at 08:38 and 08:39; the signal fills at the 08:39 open");
            Assert.That(trade.Exit, Is.EqualTo(10.7m));
        });
    }

    [Test]
    public void VwapLoss_ExitsOnlyWhenEnabled()
    {
        var bars = Premarket();
        double prev = 10;
        for (int i = 0; i < 6; i++) { bars.Add(Bar(8, 30 + i, prev, prev + 0.2, volume: 100)); prev += 0.2; }
        bars.Add(Bar(8, 36, prev, 10.05, volume: 100));
        for (int i = 37; i < 60; i++) bars.Add(Bar(8, i, 10.05, 10.05));
        var on = GapperRuleSimulator.Simulate(Config(stop: 50, vwap: true), bars, Day, Et(8, 30), 0)!;
        var off = GapperRuleSimulator.Simulate(Config(stop: 50, vwap: false), bars, Day, Et(8, 30), 0)!;
        Assert.Multiple(() =>
        {
            Assert.That(on.ExitReason, Is.EqualTo("vwap-loss"));
            Assert.That(on.ExitUtc, Is.EqualTo(Et(8, 37)));
            Assert.That(off.ExitReason, Is.Not.EqualTo("vwap-loss"));
        });
    }

    [Test]
    public void HaltFill_UsesTheFirstBarAfterTheGap()
    {
        var bars = Premarket();
        double[] closes = [10.1, 10.3, 10.6, 10.9, 11.1, 11.0, 10.9, 10.8];
        double prev = 10;
        for (int i = 0; i < closes.Length; i++) { bars.Add(Bar(8, 30 + i, prev, closes[i])); prev = closes[i]; }
        bars.Add(Bar(8, 50, 9.5, 9.6));
        var trade = GapperRuleSimulator.Simulate(Config(stop: 50), bars, Day, Et(8, 30), 0)!;
        Assert.Multiple(() =>
        {
            Assert.That(trade.ExitReason, Is.EqualTo("velocity-death"));
            Assert.That(trade.HaltFill, Is.True, "no bar printed at the signal minute: a bar-gap halt");
            Assert.That(trade.ExitUtc, Is.EqualTo(Et(8, 50)));
            Assert.That(trade.Exit, Is.EqualTo(9.5m));
        });
    }

    [Test]
    public void Simulate_ReturnsNull_WithoutABarAfterThePass()
    {
        Assert.That(GapperRuleSimulator.Simulate(Config(), Premarket(), Day, Et(8, 30), 0), Is.Null);
        Assert.That(new GapperRuleConfig(3, 0.5, 3, 5, new TimeSpan(9, 28, 0), true).Passes(2.9, 1), Is.False);
        Assert.That(new GapperRuleConfig(3, 0.5, 3, 5, new TimeSpan(9, 28, 0), true).Passes(3, null), Is.False, "a missing reading never passes");
        Assert.That(new GapperRuleConfig(3, 0.5, 3, 5, new TimeSpan(9, 28, 0), true).Passes(3, 0.5), Is.True);
    }

    [Test]
    public void CostModel_SpreadTimesMultiplier_PlusTwiceImpact_WithTheFloor()
    {
        var full = GapperCostModel.Estimate(new GapperCostInputs(0.01, 2.0, 100_000, 0.05, 2.0, 1000, 1.0, 0.005));
        var floored = GapperCostModel.Estimate(new GapperCostInputs(0.0005, 1.0, 10_000_000, 0.01, 2.0, 1000, 1.0, 0.005));
        var missing = GapperCostModel.Estimate(new GapperCostInputs(null, 2.0, 100_000, 0.05, 2.0, 1000, 1.0, 0.005));
        double impact = 1.0 * 0.05 * Math.Sqrt(500.0 / 100_000);
        Assert.Multiple(() =>
        {
            Assert.That(full.Spread, Is.EqualTo(0.02).Within(1e-12));
            Assert.That(full.ImpactPerSide, Is.EqualTo(impact).Within(1e-12));
            Assert.That(full.RoundTrip, Is.EqualTo(0.02 + 2 * impact).Within(1e-12));
            Assert.That(full.Floored, Is.False);
            Assert.That(floored.RoundTrip, Is.EqualTo(0.005));
            Assert.That(floored.Floored, Is.True);
            Assert.That(missing.SpreadMissing, Is.True);
            Assert.That(missing.Spread, Is.EqualTo(0));
            Assert.That(GapperCostModel.Sigma([10m, 10.5m, 10m, 10.5m, 10m]), Is.GreaterThan(0));
            Assert.That(GapperCostModel.Sigma([10m, 11m]), Is.Null);
        });
    }
}
