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

    [Fact]
    public void StrategyRegistry_GetAll_ReturnsAtLeastFourStrategies()
    {
        var registry = new StrategyRegistry();
        Assert.True(registry.GetAll().Count >= 4, "Expected ITI, LowHigh, PremarketBreakout, MomentumDecay");
    }

    [Fact]
    public void StrategyRegistry_Get_KnownName_ReturnsStrategy()
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get("ITI");
        Assert.NotNull(strategy);
        Assert.Equal("ITI", strategy.Name);
    }

    [Fact]
    public void StrategyRegistry_Get_UnknownName_ReturnsNull()
    {
        var registry = new StrategyRegistry();
        Assert.Null(registry.Get("DoesNotExist"));
    }

    // ── All strategies: safety contract ──────────────────────────────────────────

    [Theory]
    [InlineData("ITI")]
    [InlineData("LowHigh")]
    [InlineData("PremarketBreakout")]
    [InlineData("MomentumDecay")]
    public void Strategy_EmptyCandles_ReturnsNoSignals(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var signals = strategy.Evaluate("TEST", [], RthContext());
        Assert.Empty(signals);
    }

    [Theory]
    [InlineData("ITI")]
    [InlineData("LowHigh")]
    [InlineData("PremarketBreakout")]
    [InlineData("MomentumDecay")]
    public void Strategy_FewCandles_DoesNotThrow(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var candles = FlatCandles(5);
        var ex = Record.Exception(() => strategy.Evaluate("TEST", candles, RthContext()));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("ITI")]
    [InlineData("LowHigh")]
    [InlineData("PremarketBreakout")]
    [InlineData("MomentumDecay")]
    public void Strategy_FlatMarket_ConfidenceInRange(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var candles = FlatCandles(60);
        var signals = strategy.Evaluate("TEST", candles, RthContext());
        Assert.All(signals, s =>
        {
            Assert.InRange(s.ConfidencePercent, 0m, 100m);
            Assert.False(string.IsNullOrEmpty(s.Symbol));
            Assert.False(string.IsNullOrEmpty(s.StrategyName));
        });
    }

    // ── TradeSignal contract ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("ITI")]
    [InlineData("LowHigh")]
    [InlineData("PremarketBreakout")]
    [InlineData("MomentumDecay")]
    public void Strategy_SignalSymbolMatchesInput(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var candles = TrendingCandles(60, 100, 0.5m);
        var signals = strategy.Evaluate("AAPL", candles, RthContext());
        Assert.All(signals, s => Assert.Equal("AAPL", s.Symbol));
    }

    [Theory]
    [InlineData("ITI")]
    [InlineData("LowHigh")]
    [InlineData("MomentumDecay")]
    public void Strategy_LongSignal_EntryAboveStop(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var candles = TrendingCandles(60, 100, 0.5m);
        var signals = strategy.Evaluate("TEST", candles, RthContext())
            .Where(s => s.Direction == TradeDirection.Long && s.SuggestedStop > 0).ToList();
        Assert.All(signals, s => Assert.True(s.SuggestedEntry > s.SuggestedStop,
            $"Long entry ${s.SuggestedEntry} should be above stop ${s.SuggestedStop}"));
    }

    [Theory]
    [InlineData("ITI")]
    [InlineData("MomentumDecay")]
    public void Strategy_ShortSignal_EntryBelowStop(string name)
    {
        var registry = new StrategyRegistry();
        var strategy = registry.Get(name)!;
        var candles = TrendingCandles(60, 200, -0.5m);  // downtrend
        var signals = strategy.Evaluate("TEST", candles, RthContext())
            .Where(s => s.Direction == TradeDirection.Short && s.SuggestedStop > 0).ToList();
        Assert.All(signals, s => Assert.True(s.SuggestedEntry < s.SuggestedStop,
            $"Short entry ${s.SuggestedEntry} should be below stop ${s.SuggestedStop}"));
    }

    // ── PremarketBreakout specific ────────────────────────────────────────────────

    [Fact]
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
        Assert.Empty(signals);
    }

    [Fact]
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
        Assert.Empty(signals);
    }

    // ── MomentumDecay specific ────────────────────────────────────────────────────

    [Fact]
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
        Assert.True(longs <= 1,  $"Expected at most 1 Long but got {longs}");
        Assert.True(shorts <= 1, $"Expected at most 1 Short but got {shorts}");
    }

    [Fact]
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
        Assert.Empty(signals);
    }
}
