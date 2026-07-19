using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Full mock-gap-day lifecycle (IP-US-K6, RFC 0002): drives the exact brain
/// the Monitor runs — MockDataFeed's deterministic premarket gap arc →
/// previous close → snapshot → entry conditions → simulated fill →
/// GapperExitEvaluator — across a premarket morning. The Monitor's wall-clock
/// session gate and broker I/O are exercised separately (live console run +
/// Brokers.Tests); everything between is proven here.
/// </summary>
public class GapperLifecycleTests
{
    // A Friday. "GAP*" symbols always gap in MockDataFeed. Times below are
    // constructed IN Eastern then converted to UTC so the test is immune to
    // the host timezone and to EDT/EST.
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static readonly TimeZoneInfo Eastern = MarketTime.Eastern;

    private static DateTime EtToUtc(int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(Day.ToDateTime(new TimeOnly(hour, minute)), DateTimeKind.Unspecified), Eastern);

    private static GapperProfile Profile() => new()
    {
        Id = "classic-gapper", Name = "Classic",
        MinGapPercent = 5, MinVolumeRatio = 2, MinPrice = 1, MaxPrice = 5000,
        EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
        StopLossPercent = 8, PeakGivebackPercent = 25,
        ArmExitAtEt = "09:12", SellByEt = "09:28", DefaultNotional = 1000m,
    };

    private static async Task<List<Candle>> FetchAsync(MockDataFeed feed, string symbol, DateTime startUtc, DateTime endUtc)
    {
        var list = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, startUtc, endUtc, TimeSpan.FromMinutes(1)))
            list.Add(c);
        return list;
    }

    [Test]
    public async Task MockGapDay_EntryFires_InPremarket_ThenGivebackExit_BeforeTheBell()
    {
        var feed = new MockDataFeed();
        const string symbol = "GAPT";
        var def = ScriptParser.ParseScript(GapperScriptFactory.ToScript(symbol, Profile()))!;

        // Previous close from the feed's daily bars — the same call the
        // Monitor makes for gap math.
        var previousClose = await ((IMarketDataFeed)feed).GetPreviousCloseAsync(symbol, EtToUtc(6, 0));
        Assert.That(previousClose, Is.Not.Null, "mock daily bars must supply a previous close");

        // ── Phase 1: hunt the entry through the premarket ──
        DateTime? entryUtc = null;
        double entryPrice = 0;
        for (var minute = 0; minute < 120 && entryUtc is null; minute++)
        {
            var nowUtc = EtToUtc(6, 0).AddMinutes(minute); // hunt from 06:00 ET
            var candles = await FetchAsync(feed, symbol, nowUtc.AddHours(-3), nowUtc);
            var snapshot = IndicatorSnapshotBuilder.BuildWithEmas(symbol, candles, [], previousClose);

            if (def.EntryConditions.All(c => c.Evaluate(snapshot)))
            {
                entryUtc = nowUtc;
                entryPrice = snapshot.Price;
            }
        }

        Assert.That(entryUtc, Is.Not.Null,
            "the gap screen (gap %, volume burst, price band, entry window) must all pass on some premarket tick");
        Assert.That(entryPrice, Is.GreaterThan((double)previousClose!.Value * 1.05),
            "entry price reflects a ≥5% gap over the previous close");

        // ── Phase 2: hold through the run; no exit while momentum is intact ──
        var holdUtc = EtToUtc(8, 30);
        var holdCandles = await FetchAsync(feed, symbol, entryUtc.Value, holdUtc);
        var holdDecision = GapperExitEvaluator.Evaluate(def, entryPrice, entryUtc.Value, holdCandles, holdUtc);
        Assert.That(holdDecision, Is.Null, "mid-run, before the arm time, the gapper keeps holding");

        // ── Phase 3: the rollover after 9:12 ET trips the giveback exit before 9:28 ──
        GapperExitDecision? exit = null;
        DateTime exitUtc = default;
        for (var minute = 0; minute <= 16 && exit is null; minute++)
        {
            var nowUtc = EtToUtc(9, 12).AddMinutes(minute);
            var candles = await FetchAsync(feed, symbol, entryUtc.Value, nowUtc);
            exit = GapperExitEvaluator.Evaluate(def, entryPrice, entryUtc.Value, candles, nowUtc);
            exitUtc = nowUtc;
        }

        Assert.That(exit, Is.Not.Null, "the sell-off must trigger before the bell");
        Assert.Multiple(() =>
        {
            Assert.That(exit!.Reason, Is.EqualTo(GapperExitReason.PeakGiveback).Or.EqualTo(GapperExitReason.SellByTime),
                "momentum rollover sells it off; the hard sell-by is the fallback");
            Assert.That(MarketTime.ToEasternTimeOfDay(exitUtc), Is.LessThan(new TimeSpan(9, 30, 0)),
                "flat BEFORE the 9:30 bell");
            Assert.That(exit.PeakPrice, Is.GreaterThanOrEqualTo(entryPrice), "peak tracked from entry");
        });
    }

    /// <summary>
    /// Minimal daily-bar-only feed with a DISTINCT close per day, stamped at
    /// UTC midnight (the convention many bar providers use for "1Day"
    /// granularity). MockDataFeed's own daily bars are flat-valued, so they
    /// can't reveal an off-by-one-day bug in the date comparison; this fake
    /// can, because picking the wrong day returns a visibly wrong value.
    /// </summary>
    private sealed class MidnightStampedDailyFeed : IMarketDataFeed
    {
        public string FeedName => "FakeDaily";

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(
            string symbol, DateTime startUtc, DateTime endUtc, TimeSpan candleSize,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            // 07-15 -> 10.00, 07-16 -> 20.00, 07-17 -> 30.00 (today, must be excluded).
            var closes = new (DateTime day, decimal close)[]
            {
                (new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), 10.00m),
                (new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc), 20.00m),
                (new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc), 30.00m),
            };
            foreach (var (day, close) in closes)
            {
                if (day < startUtc || day >= endUtc) continue;
                yield return new Candle { Symbol = symbol, StartUtc = day, EndUtc = day.AddDays(1), Open = close, High = close, Low = close, Close = close, Volume = 1000 };
            }
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) =>
            Task.FromResult<LatestPrice?>(null);
    }

    [Test]
    public async Task GetPreviousCloseAsync_UtcMidnightStampedDailyBars_PicksYesterdayNotToday()
    {
        // "Now" = 05:00 ET on 07-17 (premarket) — a UTC-midnight-stamped bar
        // for 07-17 converts to ~19:00-20:00 ET on 07-16 when run through the
        // Eastern clock, which used to make the interface's date comparison
        // (incorrectly) treat that bar as belonging to 07-16 and, worse,
        // could let it overwrite 07-16's real close as "previous" once a
        // same-shaped 07-17 bar iterated past it. Comparing raw UTC dates
        // instead must correctly land on 07-16's close (20.00), not 07-17's
        // still-forming value (30.00) and not 07-15's (10.00).
        var feed = new MidnightStampedDailyFeed();
        var nowUtc = new DateTime(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc); // 05:00 ET

        var previousClose = await ((IMarketDataFeed)feed).GetPreviousCloseAsync("TEST", nowUtc);

        Assert.That(previousClose, Is.EqualTo(20.00m));
    }

    [Test]
    public async Task MockGapDay_HardSellBy_FlattensEvenIfMomentumNeverRollsOver()
    {
        // Giveback dialed absurdly loose (99%) so only the sell-by can fire —
        // proving the unconditional flatten-before-the-bell fallback.
        var feed = new MockDataFeed();
        const string symbol = "GAPQ";
        var p = Profile();
        p.PeakGivebackPercent = 99;
        var def = ScriptParser.ParseScript(GapperScriptFactory.ToScript(symbol, p))!;

        var entryUtc = EtToUtc(6, 0);
        var nowUtc = EtToUtc(9, 28);
        var candles = await FetchAsync(feed, symbol, entryUtc, nowUtc);
        var entryPrice = (double)candles[0].Close;

        var exit = GapperExitEvaluator.Evaluate(def, entryPrice, entryUtc, candles, nowUtc);

        Assert.That(exit, Is.Not.Null);
        Assert.That(exit!.Reason, Is.EqualTo(GapperExitReason.SellByTime));
    }
}
