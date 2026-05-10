using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies;

namespace IdiotProof.NUnitTests;

/// <summary>
/// End-to-end smoke tests for DslStrategy — the IStrategy adapter that wraps
/// a fluent StrategyDefinition for the Monitor. We feed it synthetic candles,
/// build a known strategy, and verify the right TradeSignal (or none) is
/// produced. These are the closest tests to "user pipeline works" — they cover
/// the snapshot builder, the condition evaluator, and the adapter's signal
/// emission path together.
/// </summary>
[TestFixture]
public class DslStrategyTests
{
    private static IReadOnlyList<Candle> SyntheticUptrend(int count = 40)
    {
        var list = new List<Candle>(count);
        var t = new DateTime(2026, 5, 1, 13, 30, 0, DateTimeKind.Utc);
        for (int i = 0; i < count; i++)
        {
            var p = 100m + i * 0.5m;
            list.Add(new Candle
            {
                Symbol   = "TEST",
                StartUtc = t.AddMinutes(5 * i),
                EndUtc   = t.AddMinutes(5 * i + 5),
                Open  = p,
                High  = p + 0.3m,
                Low   = p - 0.3m,
                Close = p,
                Volume = 1500m,
            });
        }
        return list;
    }

    private static StrategyContext Ctx() => new()
    {
        Timezone = TimeZoneInfo.Utc,
        EvaluationTimeUtc = DateTime.UtcNow,
    };

    [Test]
    public void Evaluate_SymbolMismatch_EmitsNoSignals()
    {
        var def = Stock.Ticker("AAPL").Long().Build();
        var strat = new DslStrategy(def);
        var signals = strat.Evaluate("MSFT", SyntheticUptrend(), Ctx());
        Assert.That(signals, Is.Empty);
    }

    [Test]
    public void Evaluate_NoCandles_EmitsNoSignals()
    {
        var def = Stock.Ticker("TEST").IsAboveVwap().Long().Build();
        var strat = new DslStrategy(def);
        var signals = strat.Evaluate("TEST", [], Ctx());
        Assert.That(signals, Is.Empty);
    }

    [Test]
    public void Evaluate_AllConditionsPass_EmitsSignal()
    {
        // Uptrend ramp: price stays above VWAP, ADX strong, EMA stack proper.
        var def = Stock.Ticker("TEST")
            .IsAboveVwap()
            .Long()
            .Build();

        var strat = new DslStrategy(def);
        var signals = strat.Evaluate("TEST", SyntheticUptrend(), Ctx());

        Assert.That(signals, Has.Count.EqualTo(1));
        Assert.That(signals[0].Direction, Is.EqualTo(TradeDirection.Long));
        Assert.That(signals[0].Symbol, Is.EqualTo("TEST"));
    }

    [Test]
    public void Evaluate_StopAndTargetPropagate_ToSignal()
    {
        var def = Stock.Ticker("TEST")
            .IsAboveVwap()
            .Long()
            .StopLoss(95.0)
            .TakeProfit(125.0)
            .Build();

        var strat = new DslStrategy(def);
        var signals = strat.Evaluate("TEST", SyntheticUptrend(), Ctx());

        Assert.That(signals, Has.Count.EqualTo(1));
        Assert.That(signals[0].SuggestedStop, Is.EqualTo(95.0m).Within(0.01));
        Assert.That(signals[0].Targets, Has.Count.EqualTo(1));
        Assert.That(signals[0].Targets[0], Is.EqualTo(125.0m).Within(0.01));
    }

    [Test]
    public void Evaluate_StrategyType_IsFluentDsl()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        var strat = new DslStrategy(def);
        Assert.That(strat.Type, Is.EqualTo(StrategyType.FluentDsl));
    }
}
