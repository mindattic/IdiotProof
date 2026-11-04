using System.Runtime.CompilerServices;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Core.Tests;

/// <summary>
/// The /backtest page's day run (IP-US-J1..J3): the chosen ET day's minute candles come from the
/// feed for the strategy's session, the report carries a per-candle condition table, and the
/// summary totals signals, entries/exits and P&amp;L.
/// </summary>
[TestFixture]
public sealed class BacktestDayRunnerTests
{
    private static readonly DateOnly Day = new(2026, 5, 29); // a Friday in EDT (UTC-4)

    private sealed class RecordingFeed(IReadOnlyList<Candle> bars, decimal? previousClose = 9m) : IMarketDataFeed
    {
        public string FeedName => "Recording";
        public List<(string Symbol, DateTime Start, DateTime End, TimeSpan Size)> Requests { get; } = [];

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Requests.Add((symbol, startUtc, endUtc, candleSize));
            foreach (var b in bars.Where(b => b.StartUtc >= startUtc && b.StartUtc < endUtc)) yield return b;
            await Task.CompletedTask;
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);
        public Task<decimal?> GetPreviousCloseAsync(string symbol, DateTime beforeUtc, CancellationToken ct = default) => Task.FromResult(previousClose);
    }

    private static Candle Bar(DateTime startUtc, decimal o, decimal h, decimal l, decimal c) => new()
    {
        Symbol = "TST", StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1), Open = o, High = h, Low = l, Close = c, Volume = 1000m,
    };

    private static readonly DateTime Open930 = new(2026, 5, 29, 13, 30, 0, DateTimeKind.Utc);

    /// <summary>Breakout over 10, pullback to 9, target 12 — one premarket bar that must be ignored for an RTH strategy.</summary>
    private static List<Candle> Day1Bars() =>
    [
        Bar(Open930.AddHours(-1), 11m, 13m, 8m, 12m),          // 08:30 ET premarket: outside RTH, must not be fetched
        Bar(Open930,              9.0m,  9.5m,  8.8m, 9.3m),
        Bar(Open930.AddMinutes(1), 9.3m, 10.5m, 9.2m, 10.2m),  // breakout latches
        Bar(Open930.AddMinutes(2), 10.2m, 10.3m, 8.9m, 9.2m),  // pullback latches -> enter 9.20
        Bar(Open930.AddMinutes(3), 9.2m, 12.5m, 9.1m, 12.3m),  // target 12
    ];

    private static StrategyDefinition BreakoutPullback() =>
        Stock.Ticker("TST").Breakout(10).Pullback(9).Long().TakeProfit(12).StopLoss(8).Build();

    [Test]
    public void SessionWindow_FollowsTheStrategySession_OnTheEasternClock_AcrossDst()
    {
        var rthSummer = BacktestDayRunner.SessionWindowUtc(Day, TradingSession.RTH);
        var rthWinter = BacktestDayRunner.SessionWindowUtc(new DateOnly(2026, 1, 15), TradingSession.RTH);
        var pre = BacktestDayRunner.SessionWindowUtc(Day, TradingSession.Premarket);
        var ext = BacktestDayRunner.SessionWindowUtc(Day, TradingSession.Extended);
        Assert.Multiple(() =>
        {
            Assert.That(rthSummer, Is.EqualTo((new DateTime(2026, 5, 29, 13, 30, 0), new DateTime(2026, 5, 29, 20, 0, 0))));
            Assert.That(rthWinter, Is.EqualTo((new DateTime(2026, 1, 15, 14, 30, 0), new DateTime(2026, 1, 15, 21, 0, 0))));
            Assert.That(pre, Is.EqualTo((new DateTime(2026, 5, 29, 8, 0, 0), new DateTime(2026, 5, 29, 13, 30, 0))));
            Assert.That(ext, Is.EqualTo((new DateTime(2026, 5, 29, 8, 0, 0), new DateTime(2026, 5, 30, 0, 0, 0))));
        });
    }

    [Test]
    public async Task RunAsync_FetchesTheChosenDaysMinuteCandles_ForTheSession_AndReplaysThem()
    {
        var feed = new RecordingFeed(Day1Bars());
        var report = await BacktestDayRunner.RunAsync(BreakoutPullback(), Day, feed);

        Assert.That(feed.Requests, Has.Count.EqualTo(1));
        var req = feed.Requests[0];
        Assert.Multiple(() =>
        {
            Assert.That(req.Symbol, Is.EqualTo("TST"));
            Assert.That(req.Size, Is.EqualTo(TimeSpan.FromMinutes(1)));
            Assert.That(req.Start, Is.EqualTo(Open930));
            Assert.That(report.BarsProcessed, Is.EqualTo(4), "the 08:30 ET bar sits outside the RTH window and is never replayed");
            Assert.That(report.FirstBarUtc, Is.EqualTo(Open930));
        });
    }

    [Test]
    public async Task RunAsync_ReportsAPerCandleConditionTable()
    {
        var report = await BacktestDayRunner.RunAsync(BreakoutPullback(), Day, new RecordingFeed(Day1Bars()));

        Assert.That(report.ConditionTable, Has.Count.EqualTo(4));
        var rows = report.ConditionTable;
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Labels, Is.EqualTo(new[] { "Breakout(10)", "Pullback(9)" }));
            Assert.That(rows.Select(r => r.Utc), Is.EqualTo(new[] { Open930, Open930.AddMinutes(1), Open930.AddMinutes(2), Open930.AddMinutes(3) }));
            Assert.That(rows[0].Results, Is.EqualTo(new[] { false, false }));
            Assert.That(rows[1].Results[0], Is.True, "breakout passes on the 10.50 high");
            Assert.That(rows[1].HasTriggerFire, Is.True);
            Assert.That(rows[1].AllSatisfied, Is.False);
            Assert.That(rows[2].AllSatisfied, Is.True);
            Assert.That(rows[2].OpenedTrade, Is.True, "the pullback bar completes the setup");
            Assert.That(rows.Count(r => r.OpenedTrade), Is.EqualTo(1));
            Assert.That(rows[2].Close, Is.EqualTo(9.2m));
        });
    }

    [Test]
    public async Task RunAsync_SummarizesSignals_EntriesExits_AndPnl()
    {
        var report = await BacktestDayRunner.RunAsync(BreakoutPullback(), Day, new RecordingFeed(Day1Bars()));

        Assert.That(report.Trades, Has.Count.EqualTo(1));
        var t = report.Trades[0];
        Assert.Multiple(() =>
        {
            Assert.That(report.Triggers, Has.Count.EqualTo(2), "signal count");
            Assert.That(t.EntryPrice, Is.EqualTo(9.2m));
            Assert.That(t.EntryUtc, Is.EqualTo(Open930.AddMinutes(2)));
            Assert.That(t.Exits[^1].Price, Is.EqualTo(12m));
            Assert.That(t.ExitReason, Is.EqualTo("Target"));
            Assert.That(report.TotalPnL, Is.EqualTo(280m));
            Assert.That(report.Wins, Is.EqualTo(1));
            Assert.That(report.NoTriggersFired, Is.False);
        });
    }

    [Test]
    public async Task RunAsync_PassesThePreviousClose_SoAGapStrategyCanFire()
    {
        var gap = Stock.Ticker("TST").IsGapUp(10).Long().StopLoss(8).TakeProfit(12).Build();
        var withClose = await BacktestDayRunner.RunAsync(gap, Day, new RecordingFeed(Day1Bars(), previousClose: 8m));
        var noClose = await BacktestDayRunner.RunAsync(gap, Day, new RecordingFeed(Day1Bars(), previousClose: null));
        Assert.Multiple(() =>
        {
            Assert.That(withClose.Trades, Is.Not.Empty, "9.30 vs 8.00 is a 16% gap");
            Assert.That(noClose.Trades, Is.Empty, "unknown previous close fails closed");
        });
    }
}
