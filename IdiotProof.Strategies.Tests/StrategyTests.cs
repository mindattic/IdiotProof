using IdiotProof.Models;
using IdiotProof.Strategies;

namespace IdiotProof.Strategies.Tests;

public class StrategyTests
{
    // ── Candle factory helpers ────────────────────────────────────────────────────

    private static readonly TimeZoneInfo Et = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private static StrategyContext RthContext()
    {
        // 10:00 AM ET = RTH
        var etNow = new DateTime(2025, 4, 15, 10, 0, 0, DateTimeKind.Unspecified);
        var utcNow = TimeZoneInfo.ConvertTimeToUtc(etNow, Et);
        return new StrategyContext { Timezone = Et, EvaluationTimeUtc = utcNow };
    }

    private static StrategyContext PremarketContext()
    {
        // 8:00 AM ET = Premarket
        var etNow = new DateTime(2025, 4, 15, 8, 0, 0, DateTimeKind.Unspecified);
        var utcNow = TimeZoneInfo.ConvertTimeToUtc(etNow, Et);
        return new StrategyContext { Timezone = Et, EvaluationTimeUtc = utcNow };
    }

    private static List<Candle> FlatCandles(int count, decimal price = 100m)
    {
        var start = new DateTime(2025, 4, 14, 9, 30, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count).Select(i => new Candle
        {
            Symbol = "TEST",
            StartUtc = start.AddMinutes(i),
            Open = price, High = price + 0.05m, Low = price - 0.05m,
            Close = price, Volume = 100_000
        }).ToList();
    }

    private static List<Candle> TrendingCandles(int count, decimal start, decimal step, decimal volume = 500_000)
    {
        var t = new DateTime(2025, 4, 14, 9, 30, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count).Select(i =>
        {
            var close = start + step * i;
            return new Candle
            {
                Symbol = "TEST",
                StartUtc = t.AddMinutes(i),
                Open = close - step * 0.5m,
                High = close + Math.Abs(step) * 0.3m,
                Low  = close - Math.Abs(step) * 0.3m,
                Close = close,
                Volume = volume
            };
        }).ToList();
    }

    // ── StrategyRegistry ──────────────────────────────────────────────────────────

    [Test]
    public void StrategyRegistry_GetAll_ReturnsAtLeastFourStrategies()
    {
        var registry = new StrategyRegistry();
        Assert.That(registry.GetAll().Count, Is.GreaterThanOrEqualTo(4), "Expected ITI, LowHigh, PremarketBreakout, MomentumDecay");
    }

    [Test]
    public void StrategyRegistry_Get_KnownName_ReturnsStrategy()
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get("ITI");
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy!.Name, Is.EqualTo("ITI"));
    }

    [Test]
    public void StrategyRegistry_Get_UnknownName_ReturnsNull()
    {
        var registry = new StrategyRegistry();
        Assert.That(registry.Get("DoesNotExist"), Is.Null);
    }

    // ── All strategies: safety contract ──────────────────────────────────────────

    [TestCase("ITI")]
    [TestCase("LowHigh")]
    [TestCase("PremarketBreakout")]
    [TestCase("MomentumDecay")]
    public void Strategy_EmptyCandles_ReturnsNoSignals(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var signals = strategy.Evaluate("TEST", [], RthContext());
        Assert.That(signals, Is.Empty);
    }

    [TestCase("ITI")]
    [TestCase("LowHigh")]
    [TestCase("PremarketBreakout")]
    [TestCase("MomentumDecay")]
    public void Strategy_FewCandles_DoesNotThrow(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var candles = FlatCandles(5);
        Assert.DoesNotThrow(() => strategy.Evaluate("TEST", candles, RthContext()));
    }

    [TestCase("ITI")]
    [TestCase("LowHigh")]
    [TestCase("PremarketBreakout")]
    [TestCase("MomentumDecay")]
    public void Strategy_FlatMarket_ConfidenceInRange(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var candles = FlatCandles(60);
        var signals = strategy.Evaluate("TEST", candles, RthContext());
        foreach (var s in signals)
        {
            Assert.That(s.ConfidencePercent, Is.InRange(0m, 100m));
            Assert.That(string.IsNullOrEmpty(s.Symbol), Is.False);
            Assert.That(string.IsNullOrEmpty(s.StrategyName), Is.False);
        }
    }

    // ── TradeSignal contract ──────────────────────────────────────────────────────

    [TestCase("ITI")]
    [TestCase("LowHigh")]
    [TestCase("PremarketBreakout")]
    [TestCase("MomentumDecay")]
    public void Strategy_SignalSymbolMatchesInput(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var candles = TrendingCandles(60, 100, 0.5m);
        var signals = strategy.Evaluate("AAPL", candles, RthContext());
        foreach (var s in signals)
            Assert.That(s.Symbol, Is.EqualTo("AAPL"));
    }

    [TestCase("ITI")]
    [TestCase("LowHigh")]
    [TestCase("MomentumDecay")]
    public void Strategy_LongSignal_EntryAboveStop(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var candles = TrendingCandles(60, 100, 0.5m);
        var signals = strategy.Evaluate("TEST", candles, RthContext())
            .Where(s => s.Direction == TradeDirection.Long && s.SuggestedStop > 0).ToList();
        foreach (var s in signals)
            Assert.That(s.SuggestedEntry, Is.GreaterThan(s.SuggestedStop),
                $"Long entry ${s.SuggestedEntry} should be above stop ${s.SuggestedStop}");
    }

    [TestCase("ITI")]
    [TestCase("MomentumDecay")]
    public void Strategy_ShortSignal_EntryBelowStop(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var candles = TrendingCandles(60, 200, -0.5m);  // downtrend
        var signals = strategy.Evaluate("TEST", candles, RthContext())
            .Where(s => s.Direction == TradeDirection.Short && s.SuggestedStop > 0).ToList();
        foreach (var s in signals)
            Assert.That(s.SuggestedEntry, Is.LessThan(s.SuggestedStop),
                $"Short entry ${s.SuggestedEntry} should be below stop ${s.SuggestedStop}");
    }

    // ── PremarketBreakout specific ────────────────────────────────────────────────

    [Test]
    public void PremarketBreakout_AfterHoursContext_ReturnsEmpty()
    {
        // After hours (10 PM ET) is outside valid session window → no signals
        var registry = new StrategyRegistry();
        var strategy = registry.Get("PremarketBreakout")!;
        var candles = FlatCandles(60);
        var etNow = new DateTime(2025, 4, 15, 22, 0, 0, DateTimeKind.Unspecified);
        var utcNow = TimeZoneInfo.ConvertTimeToUtc(etNow, Et);
        var ctx = new StrategyContext { Timezone = Et, EvaluationTimeUtc = utcNow };
        var signals = strategy.Evaluate("TEST", candles, ctx);
        Assert.That(signals, Is.Empty);
    }

    [Test]
    public void PremarketBreakout_NoPremarketCandles_ReturnsEmpty()
    {
        // All candles have RTH timestamps → no premarket candles → no signal
        var registry = new StrategyRegistry();
        var strategy = registry.Get("PremarketBreakout")!;
        // RTH candles starting 9:30 AM ET = 1:30 PM UTC in April (EDT)
        var rthStart = new DateTime(2025, 4, 14, 13, 30, 0, DateTimeKind.Utc);
        var candles = Enumerable.Range(0, 60).Select(i => new Candle
        {
            Symbol = "TEST", StartUtc = rthStart.AddMinutes(i),
            Open = 100, High = 100.1m, Low = 99.9m, Close = 100, Volume = 100_000
        }).ToList();
        var signals = strategy.Evaluate("TEST", candles, PremarketContext());
        Assert.That(signals, Is.Empty);
    }

    // ── MomentumDecay specific ────────────────────────────────────────────────────

    [Test]
    public void MomentumDecay_ReturnsSingleSignalPerDirection()
    {
        // Strategy logic filters to strongest, so at most one signal per call
        var registry = new StrategyRegistry();
        var strategy = registry.Get("MomentumDecay")!;
        var candles = TrendingCandles(60, 100, 1.0m);
        var signals = strategy.Evaluate("TEST", candles, RthContext());
        // Strategy docs: return strongest only (or both if both ≥ 60%)
        var longs  = signals.Count(s => s.Direction == TradeDirection.Long);
        var shorts = signals.Count(s => s.Direction == TradeDirection.Short);
        Assert.That(longs,  Is.LessThanOrEqualTo(1), $"Expected at most 1 Long but got {longs}");
        Assert.That(shorts, Is.LessThanOrEqualTo(1), $"Expected at most 1 Short but got {shorts}");
    }

    [Test]
    public void MomentumDecay_ConfluenceGating_LowConfluenceNoSignal()
    {
        // Require 4 conditions — flat market satisfies fewer than 4 → no signal
        var registry = new StrategyRegistry();
        var strategy = registry.Get("MomentumDecay")!;
        var candles = FlatCandles(60);
        var ctx = new StrategyContext
        {
            Timezone = Et,
            EvaluationTimeUtc = RthContext().EvaluationTimeUtc,
            Parameters = new() { ["MinConfluence"] = 4 }
        };
        var signals = strategy.Evaluate("TEST", candles, ctx);
        Assert.That(signals, Is.Empty);
    }
}
