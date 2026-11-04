using System.Runtime.CompilerServices;
using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Evidence.Leaders;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Core.Tests;

/// <summary>
/// LeaderConfirms / NotLeaderDiverging in backtests (IP-T395): the backtester reads leaders discovered
/// from sessions before the backtested day, and each leader's move only from bars closed by the
/// current bar, so a leader-gated strategy can fire in a backtest exactly when it could live.
/// </summary>
[TestFixture]
public sealed class BacktestLeaderTests
{
    private static readonly DateOnly Day = new(2026, 5, 29);                                  // Friday, EDT
    private static readonly DateTime Open930 = new(2026, 5, 29, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime PriorOpen930 = new(2026, 5, 28, 13, 30, 0, DateTimeKind.Utc);

    private sealed class MultiFeed(Dictionary<string, List<Candle>> bars) : IMarketDataFeed
    {
        public string FeedName => "Multi";
        public List<string> Requests { get; } = [];

        public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(string symbol, DateTime startUtc, DateTime endUtc,
            TimeSpan candleSize, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Requests.Add(symbol);
            if (bars.TryGetValue(symbol, out var list))
                foreach (var b in list.Where(b => b.StartUtc >= startUtc && b.StartUtc < endUtc)) yield return b;
            await Task.CompletedTask;
        }

        public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default) => Task.FromResult<LatestPrice?>(null);
        public Task<decimal?> GetPreviousCloseAsync(string symbol, DateTime beforeUtc, CancellationToken ct = default) => Task.FromResult<decimal?>(2.5m);
    }

    private static Candle Bar(string symbol, DateTime startUtc, decimal close, decimal? open = null)
    {
        var o = open ?? close;
        return new Candle
        {
            Symbol = symbol, StartUtc = startUtc, EndUtc = startUtc.AddMinutes(1),
            Open = o, Close = close, High = Math.Max(o, close), Low = Math.Min(o, close), Volume = 1000m,
        };
    }

    private static List<Candle> Path(string symbol, DateTime start, IReadOnlyList<double> closes)
    {
        var list = new List<Candle>(closes.Count);
        for (int i = 0; i < closes.Count; i++)
            list.Add(Bar(symbol, start.AddMinutes(i), (decimal)Math.Round(closes[i], 4), i == 0 ? null : (decimal)Math.Round(closes[i - 1], 4)));
        return list;
    }

    private static List<Candle> Flat(string symbol, DateTime start, int n, double price) => Path(symbol, start, Enumerable.Repeat(price, n).ToList());

    /// <summary>A leader whose price is flat for <paramref name="flatBars"/> bars and then climbs 0.5% a bar.</summary>
    private static List<Candle> LeaderDay(int n, int flatBars, double stepPct)
    {
        var closes = new double[n];
        double p = 50;
        for (int i = 0; i < n; i++) { if (i >= flatBars) p *= 1 + stepPct; closes[i] = p; }
        return Path("LEAD", Open930, closes);
    }

    private static StrategyDefinition LeaderGated() =>
        Stock.Ticker("FOLL").LeaderConfirms(1, 0.2).Long().TakeProfit(9.0).StopLoss(1.0).Build();

    private static BacktestLeaderContext Context(List<Candle> leaderBars) =>
        new([new LeaderLink("LEAD", 1, 0.6)], new Dictionary<string, IReadOnlyList<Candle>> { ["LEAD"] = leaderBars });

    [Test]
    public void Backtest_LeaderMovedUpFirst_Fires_LeaderFalling_DoesNot()
    {
        var follower = Flat("FOLL", Open930, 40, 2.5);
        var up = StrategyBacktester.Run(LeaderGated(), follower, new BacktestOptions { Leaders = Context(LeaderDay(40, 0, 0.005)) });
        var down = StrategyBacktester.Run(LeaderGated(), follower, new BacktestOptions { Leaders = Context(LeaderDay(40, 0, -0.005)) });
        var none = StrategyBacktester.Run(LeaderGated(), follower);

        Assert.Multiple(() =>
        {
            Assert.That(up.Trades, Is.Not.Empty, "the leader climbed first, so the guard passes");
            Assert.That(down.Trades, Is.Empty, "a falling leader never confirms");
            Assert.That(none.Trades, Is.Empty, "no leader context fails closed, same as live");
        });
    }

    [Test]
    public void Backtest_NeverReadsALeaderBarThatClosesAfterTheCurrentBar()
    {
        // The leader is flat for 30 bars and starts climbing on bar 30. A backtest that peeked at the
        // next leader bar would fire on follower bar 29; reading only closed bars it fires on bar 30.
        var follower = Flat("FOLL", Open930, 45, 2.5);
        var report = StrategyBacktester.Run(LeaderGated(), follower, new BacktestOptions { Leaders = Context(LeaderDay(45, 30, 0.005)) });

        Assert.That(report.Triggers, Is.Not.Empty);
        Assert.That(report.Triggers.Min(t => t.Utc), Is.EqualTo(Open930.AddMinutes(30)));
    }

    [Test]
    public void Backtest_LeaderBarsAfterTheEntryNeverChangeIt()
    {
        var follower = Flat("FOLL", Open930, 45, 2.5);
        var calm = LeaderDay(45, 0, 0.005);
        var wild = calm.Take(20).Concat(LeaderDay(45, 0, -0.03).Skip(20)).ToList();
        var a = StrategyBacktester.Run(LeaderGated(), follower, new BacktestOptions { Leaders = Context(calm) });
        var b = StrategyBacktester.Run(LeaderGated(), follower, new BacktestOptions { Leaders = Context(wild) });

        Assert.That(b.Triggers.Min(t => t.Utc), Is.EqualTo(a.Triggers.Min(t => t.Utc)));
        Assert.That(b.Trades[0].EntryUtc, Is.EqualTo(a.Trades[0].EntryUtc));
    }

    /// <summary>One prior RTH session where FOLL repeats LEAD's move one bar later, plus the backtested day.</summary>
    private static Dictionary<string, List<Candle>> PlantedHistory(List<Candle> leaderToday)
    {
        var rng = new Random(5);
        int n = 390;
        var lead = new double[n];
        var foll = new double[n];
        for (int i = 1; i < n; i++)
        {
            lead[i] = 0.004 * Gaussian(rng);
            foll[i] = 0.8 * lead[i - 1] + 0.001 * Gaussian(rng);
        }
        return new Dictionary<string, List<Candle>>
        {
            ["LEAD"] = Path("LEAD", PriorOpen930, Prices(lead, 50)).Concat(leaderToday).ToList(),
            ["FOLL"] = Path("FOLL", PriorOpen930, Prices(foll, 2.5)).Concat(Flat("FOLL", Open930, 40, 2.5)).ToList(),
        };
    }

    private static LeaderContextCache Cache(IMarketDataFeed feed) => new(feed, null, () => "LEAD", NullLogger.Instance);

    [Test]
    public async Task DayRunner_DiscoversLeadersFromPriorSessions_AndFiresOnlyWhenTheLeaderMovedFirst()
    {
        var upFeed = new MultiFeed(PlantedHistory(LeaderDay(40, 0, 0.005)));
        var downFeed = new MultiFeed(PlantedHistory(LeaderDay(40, 0, -0.005)));

        var (up, _) = await BacktestDayRunner.RunWithCandlesAsync(LeaderGated(), Day, upFeed, default, Cache(upFeed));
        var (down, _) = await BacktestDayRunner.RunWithCandlesAsync(LeaderGated(), Day, downFeed, default, Cache(downFeed));

        Assert.Multiple(() =>
        {
            Assert.That(up.Trades, Is.Not.Empty, up.Note);
            Assert.That(down.Trades, Is.Empty);
        });
    }

    [Test]
    public async Task DayRunner_LeaderOnlyCoupledOnTheBacktestedDay_IsNeverDiscovered()
    {
        // No prior history at all: a coupling visible only today must not pick a leader, so the guard blocks.
        var feed = new MultiFeed(new Dictionary<string, List<Candle>>
        {
            ["LEAD"] = LeaderDay(40, 0, 0.005),
            ["FOLL"] = Flat("FOLL", Open930, 40, 2.5),
        });
        var (report, _) = await BacktestDayRunner.RunWithCandlesAsync(LeaderGated(), Day, feed, default, Cache(feed));

        Assert.That(report.Trades, Is.Empty);
        Assert.That(report.Note, Does.Contain("No leader was measured"));
    }

    [Test]
    public async Task DayRunner_StrategyWithoutLeaderGuards_FetchesOnlyItsOwnBars()
    {
        var feed = new MultiFeed(PlantedHistory(LeaderDay(40, 0, 0.005)));
        var plain = Stock.Ticker("FOLL").Breakout(3).Long().TakeProfit(9.0).StopLoss(1.0).Build();

        await BacktestDayRunner.RunWithCandlesAsync(plain, Day, feed, default, Cache(feed));

        Assert.That(feed.Requests, Is.EqualTo(new[] { "FOLL" }));
    }

    private static double[] Prices(double[] returns, double start)
    {
        var p = new double[returns.Length];
        double x = start;
        for (int i = 0; i < returns.Length; i++) { x *= 1 + returns[i]; p[i] = x; }
        return p;
    }

    private static double Gaussian(Random rng) =>
        Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
}
