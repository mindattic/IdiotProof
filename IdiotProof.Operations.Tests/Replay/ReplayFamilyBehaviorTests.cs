using IdiotProof.Core.Monitoring;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Tests.Replay;

/// <summary>
/// Each built-in replay family over a synthetic 1-minute day shaped to its setup, through
/// <see cref="StrategyReplay.ReplayAsync"/> (the Monitor's evaluator + exit brain): the fire lands on
/// the bar the setup completes, exits mirror the side, and every round trip is persisted (IP-T377).
/// </summary>
public class ReplayFamilyBehaviorTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static readonly DateTime Now = new(2026, 7, 18, 16, 0, 0, DateTimeKind.Utc);

    /// <summary>Synthetic day builder: one bar per minute from a start ET time, EDT (UTC-4) in July.</summary>
    private sealed class DayBars(string symbol, int startHourEt, int startMinuteEt)
    {
        private readonly List<Candle> bars = [];
        private DateTime nextUtc = new(2026, 7, 17, startHourEt + 4, startMinuteEt, 0, DateTimeKind.Utc);
        private decimal last = 20m;

        public IReadOnlyList<Candle> Bars => bars;

        public DayBars Flat(int count, decimal price, decimal volume = 1000m)
        {
            for (int i = 0; i < count; i++) Add(price, price, price, price, volume);
            return this;
        }

        /// <summary>Close steps by <paramref name="step"/> each bar; open at the prior close, wicks hug the body.</summary>
        public DayBars Walk(int count, decimal step, decimal volume = 1000m)
        {
            for (int i = 0; i < count; i++)
            {
                var o = last; var c = last + step;
                Add(o, Math.Max(o, c), Math.Min(o, c), c, volume);
            }
            return this;
        }

        public DayBars Bar(decimal close, decimal volume, decimal? low = null, decimal? high = null)
        {
            var o = last;
            Add(o, high ?? Math.Max(o, close), low ?? Math.Min(o, close), close, volume);
            return this;
        }

        public string EtOf(int index)
        {
            var et = bars[index].StartUtc.AddHours(-4);
            return $"{et.Hour:00}:{et.Minute:00}";
        }

        public int Count => bars.Count;

        private void Add(decimal o, decimal h, decimal l, decimal c, decimal v)
        {
            bars.Add(new Candle { Symbol = symbol, StartUtc = nextUtc, EndUtc = nextUtc.AddMinutes(1), Open = o, High = h, Low = l, Close = c, Volume = v });
            nextUtc = nextUtc.AddMinutes(1);
            last = c;
        }
    }

    private static async Task<(ReplayOutcome Outcome, ReplayWorld World)> ReplayFamily(string family, DayBars day)
    {
        var w = new ReplayWorld();
        var def = StrategyReplay.ResolveFamily(family, "SYNT")!;
        Assert.That(def, Is.Not.Null, family);
        var replayDay = new ReplayDay("SYNT", Day, "sip", day.Bars, 20m);
        var outcome = await StrategyReplay.ReplayAsync(w.Db, replayDay, def, family, repeat: false, outRoot: null, Now);
        return (outcome, w);
    }

    private static void AssertLongPnl(ReplayPayoff p) =>
        Assert.That(p.PnlPct, Is.EqualTo(Math.Round((p.ExitPx - p.EntryPx) / p.EntryPx * 100.0, 2)).Within(0.011));

    private static void AssertShortPnl(ReplayPayoff p) =>
        Assert.That(p.PnlPct, Is.EqualTo(Math.Round((p.EntryPx - p.ExitPx) / p.EntryPx * 100.0, 2)).Within(0.011));

    [Test]
    public void EveryListedFamily_ResolvesToADefinition()
    {
        foreach (var f in StrategyReplay.Families)
            Assert.That(StrategyReplay.ResolveFamily(f.Id, "SYNT"), Is.Not.Null, f.Id);
        Assert.That(StrategyReplay.ResolveFamily("no-such-family", "SYNT"), Is.Null);
    }

    [Test]
    public async Task RthDrive_FiresOnTheFirstBarOfTheRamp_AndTrailsOutWhenItRollsOver()
    {
        var day = new DayBars("SYNT", 9, 30).Flat(40, 20m);
        int rampStart = day.Count;
        day.Walk(20, 0.10m, volume: 3000m)   // 20.10 → 22.00: above VWAP, above EMA9, EMA9 over EMA34, 3× volume
           .Walk(10, -0.25m);                // 22.00 → 19.50: rolls over through the 8% trail
        var (o, w) = await ReplayFamily("rthdrive", day);
        using var _ = w;

        Assert.That(o.Payoffs, Is.Not.Empty, $"best {o.BestPassed}/{o.ConditionCount}: {o.BestFail}");
        var p = o.Payoffs[0];
        Assert.Multiple(() =>
        {
            Assert.That(p.EntryEt, Is.EqualTo(day.EtOf(rampStart)), "a trend-holding entry fires on the ramp's first bar");
            Assert.That(p.Reason, Is.EqualTo("TrailingStop"));
            Assert.That(string.CompareOrdinal(p.ExitEt, p.EntryEt), Is.GreaterThan(0));
            AssertLongPnl(p);
        });
    }

    [Test]
    public async Task RthDrive_NeverFiresInThePremarket()
    {
        var day = new DayBars("SYNT", 8, 0).Flat(40, 20m).Walk(20, 0.10m, volume: 3000m);
        var (o, w) = await ReplayFamily("rthdrive", day);
        using var _ = w;
        Assert.That(o.Payoffs, Is.Empty, "the identical ramp at 08:40 ET is outside the RTH session");
    }

    [Test]
    public async Task ShortFade_SellsTheBreakdown_CoversOnTheBounce_AndInvertsPnl()
    {
        var day = new DayBars("SYNT", 9, 30).Flat(40, 20m);
        int dropStart = day.Count;
        day.Walk(10, -0.20m, volume: 3000m)  // 19.80 → 18.00 below VWAP and EMA9 on 3× volume
           .Walk(6, 0.25m);                  // bounce 18.00 → 19.50: covers on the 6% trail off the trough
        var (o, w) = await ReplayFamily("shortfade", day);
        using var _ = w;

        Assert.That(o.Payoffs, Is.Not.Empty, $"best {o.BestPassed}/{o.ConditionCount}: {o.BestFail}");
        var p = o.Payoffs[0];
        Assert.Multiple(() =>
        {
            Assert.That(p.EntryEt, Is.EqualTo(day.EtOf(dropStart)));
            Assert.That(p.Reason, Is.EqualTo("TrailingStop"), "a short covers on a bounce off the low-water mark");
            Assert.That(p.ExitPx, Is.LessThan(p.EntryPx), "covered below the short entry");
            Assert.That(p.PnlPct, Is.GreaterThan(0), "a short that covers lower is a gain");
            AssertShortPnl(p);
        });
    }

    [Test]
    public async Task ShortFade_StopsOutAboveEntry_AsALoss()
    {
        var day = new DayBars("SYNT", 9, 30).Flat(40, 20m);
        day.Walk(1, -0.20m, volume: 3000m)   // one breakdown bar fires the short at 19.80
           .Walk(3, 0.30m);                  // squeeze to 20.70: past the 3% stop above entry
        var (o, w) = await ReplayFamily("shortfade", day);
        using var _ = w;

        var p = o.Payoffs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(p.Reason, Is.EqualTo("StopLoss"));
            Assert.That(p.ExitPx, Is.GreaterThan(p.EntryPx));
            Assert.That(p.PnlPct, Is.LessThan(0));
            AssertShortPnl(p);
        });
    }

    [Test]
    public async Task Reversal_BuysTheEma9ReclaimOffTheLow_NotTheFall()
    {
        var day = new DayBars("SYNT", 9, 30).Flat(30, 20m)
            .Walk(20, -0.05m);               // 20.00 → 19.00, under EMA9 (≈0.2 lag) the whole way
        int reclaim = day.Count;
        day.Bar(19.35m, 3000m)               // closes back over EMA9, within 2% of the low, 3× volume
           .Walk(5, 0.20m);
        var (o, w) = await ReplayFamily("reversal", day);
        using var _ = w;

        Assert.That(o.Payoffs, Is.Not.Empty, $"best {o.BestPassed}/{o.ConditionCount}: {o.BestFail}");
        Assert.That(o.Payoffs[0].EntryEt, Is.EqualTo(day.EtOf(reclaim)));
        AssertLongPnl(o.Payoffs[0]);
    }

    [Test]
    public async Task EmaBreak_BuysTheEma200Reclaim_AboveVwap()
    {
        var day = new DayBars("SYNT", 4, 0).Flat(150, 20m)
            .Walk(60, -0.02m);               // 20.00 → 18.80: under EMA200 and VWAP
        int breakIdx = day.Count;
        day.Bar(20.60m, 4000m)               // one bar back over EMA200 and VWAP on volume
           .Walk(5, 0.05m);
        var (o, w) = await ReplayFamily("emabreak", day);
        using var _ = w;

        Assert.That(o.Payoffs, Is.Not.Empty, $"best {o.BestPassed}/{o.ConditionCount}: {o.BestFail}");
        Assert.That(o.Payoffs[0].EntryEt, Is.EqualTo(day.EtOf(breakIdx)));
        AssertLongPnl(o.Payoffs[0]);
    }

    [Test]
    public async Task SwingReversal_BuysTheConfirmedHigherLow_AndTakesProfitIntoThePriorHigh()
    {
        var day = new DayBars("SYNT", 9, 30)
            .Bar(20.00m, 1000m, low: 19.95m, high: 21.00m)   // high of day 21.00
            .Walk(10, -0.10m)                                 // 20.00 → 19.00: first pivot low
            .Walk(4, 0.10m)                                   // 19.40
            .Walk(2, -0.05m)                                  // 19.30: second, higher low
            .Walk(3, 0.05m);                                  // 19.45: three bars up confirm the pivot
        int firstChance = day.Count;
        day.Bar(19.55m, 3000m)                                // near support on volume
           .Walk(15, 0.10m);                                  // run into the 21.00 prior high
        var (o, w) = await ReplayFamily("swingreversal", day);
        using var _ = w;

        Assert.That(o.Payoffs, Is.Not.Empty, $"best {o.BestPassed}/{o.ConditionCount}: {o.BestFail}");
        var p = o.Payoffs[0];
        Assert.Multiple(() =>
        {
            Assert.That(p.EntryEt, Is.EqualTo(day.EtOf(firstChance)));
            Assert.That(p.Reason, Is.EqualTo("TargetHit"), "ExitAtPriorHigh sells into the earlier high of day");
            Assert.That(p.ExitPx, Is.GreaterThanOrEqualTo(21.00 * 0.997));
            AssertLongPnl(p);
        });
    }

    [Test]
    public async Task EveryRoundTrip_IsPersisted_WithItsFeatureRows()
    {
        var day = new DayBars("SYNT", 9, 30).Flat(40, 20m).Walk(20, 0.10m, volume: 3000m).Walk(10, -0.25m);
        var (o, w) = await ReplayFamily("rthdrive", day);
        using var _ = w;

        await using var db = await w.Db.CreateDbContextAsync();
        var run = await db.ReplayRuns.SingleAsync();
        var trades = await db.ReplayTrades.Where(t => t.ReplayRunId == run.Id).ToListAsync();
        var bars = await db.ReplayBars.CountAsync(b => b.ReplayRunId == run.Id);
        Assert.Multiple(() =>
        {
            Assert.That(run.Fired, Is.True);
            Assert.That(run.PayoffCount, Is.EqualTo(o.Payoffs.Count));
            Assert.That(run.TotalPnl, Is.EqualTo(Math.Round(o.Payoffs.Sum(p => p.PnlPct), 2)).Within(0.02));
            Assert.That(trades, Has.Count.EqualTo(o.Payoffs.Count), "one ReplayTrade per round trip");
            Assert.That(bars, Is.EqualTo(day.Count), "one ReplayBar per minute replayed");
            Assert.That(trades[0].Won, Is.EqualTo(o.Payoffs[0].PnlPct > 0));
        });
    }
}
