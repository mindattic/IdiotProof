using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies;
using IdiotProof.Strategies.Backtesting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Verifies the bundled sample strategies (NCI / ERNA / SUNE) build with the
/// expected shape, that their IdiotScript parses back, and that authored strategies
/// survive a serialize→parse round trip. These also guard the round-trip fixes:
/// VWAP / EMA-stack conditions, notional sizing, multi-target take-profit, and the
/// quoted/unquoted Ticker token all used to be dropped on reload.
/// </summary>
public class SampleStrategyBuildTests
{
    private static IEnumerable<StrategyDefinition> Samples() => Strategies_20260217.GetAllStrategies();

    [Test]
    public void AllSamples_Build_WithLongDirection_StopAndTargets()
    {
        foreach (var def in Samples())
        {
            Assert.Multiple(() =>
            {
                Assert.That(def.Symbol, Is.Not.Empty, "sample has a symbol");
                Assert.That(def.Direction, Is.EqualTo(TradeDirection.Long));
                Assert.That(def.EntryConditions, Is.Not.Empty, $"{def.Symbol} has entry conditions");
                Assert.That(def.StopLossPrice, Is.Not.Null, $"{def.Symbol} has a stop");
                Assert.That(def.TakeProfitTargets, Is.Not.Empty, $"{def.Symbol} scales out to targets");
                Assert.That(def.ShouldRepeat, Is.True, $"{def.Symbol} re-arms with Repeat()");
            });
        }
    }

    [Test]
    public void NciScript_ParsesBack_WithBreakoutPullbackTargetsAndStop()
    {
        var parsed = ScriptParser.ParseScript(Strategies_20260217.NCI_Script());

        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Symbol, Is.EqualTo("NCI"));
            Assert.That(parsed.EntryConditions.OfType<PatternCondition>()
                              .Any(p => p.Type == PatternType.Breakout && p.Level == 3.68), Is.True,
                              "Breakout(3.68) trigger survives parsing");
            Assert.That(parsed.EntryConditions.OfType<PatternCondition>()
                              .Any(p => p.Type == PatternType.Pullback), Is.True);
            Assert.That(parsed.TakeProfitTargets, Has.Count.EqualTo(2));
            Assert.That(parsed.StopLossPrice, Is.EqualTo(3.50));
            Assert.That(parsed.ShouldRepeat, Is.True);
        });
    }

    [Test]
    public void RoundTrip_PreservesVwapAndEmaStackConditions()
    {
        // VWAP -> "IsVwapAbove()" and EmaStack -> "IsEmaStack(...)" had no parser case
        // and were silently dropped on reload. Build → serialize → parse must keep them.
        var builder = Stock.Ticker("AAPL")
            .RequireAdxAbove(20)
            .RequireEmaStack(9, 31)
            .OnReclaim(9)
            .WithVolumeConfirm(1.2)
            .IsAboveVwap()
            .Long();

        var script = builder.ToScript();
        var parsed = ScriptParser.ParseScript(script);

        Assert.That(parsed, Is.Not.Null);
        var labels = parsed!.EntryConditions.Select(c => c.ToScript()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(labels, Has.Count.EqualTo(5), "all five entry conditions survive the round trip");
            Assert.That(labels, Does.Contain("IsAboveVwap()"));
            Assert.That(labels, Does.Contain("RequireEmaStack(9, 31)"));
            Assert.That(labels, Does.Contain("OnReclaim(9)"));
            Assert.That(labels, Does.Contain("IsAdxAbove(20)"));
            Assert.That(labels, Does.Contain("IsVolumeAbove(1.2)"));
        });
    }

    [Test]
    public void RoundTrip_PreservesNotionalSizing()
    {
        var builder = Stock.Ticker("TSLA").IsAboveVwap().Long().Quantity(1000m); // $1000 notional
        var parsed = ScriptParser.ParseScript(builder.ToScript());

        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.IsNotional, Is.True, "notional flag survives");
            Assert.That(parsed.NotionalAmount, Is.EqualTo(1000m));
            Assert.That(parsed.Quantity, Is.EqualTo(0), "share count stays 0 for a notional order");
        });
    }

    [Test]
    public void RoundTrip_PreservesMultiTargetTakeProfitAndStop()
    {
        var builder = Stock.Ticker("SUNE").Breakout(2.42).HoldsAbove(2.30).Long()
                            .TakeProfit(2.85, 3.20, 4.20).StopLoss(2.25);

        var parsed = ScriptParser.ParseScript(builder.ToScript());

        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.TakeProfitTargets, Has.Count.EqualTo(3), "all three targets survive");
            Assert.That(parsed.TakeProfitTargets.Select(t => t.Price),
                        Is.EqualTo(new[] { 2.85, 3.20, 4.20 }));
            Assert.That(parsed.StopLossPrice, Is.EqualTo(2.25));
            Assert.That(parsed.EntryConditions.OfType<PatternCondition>()
                              .Any(p => p.Type == PatternType.Breakout && p.Level == 2.42), Is.True);
        });
    }

    [Test]
    public void Backtest_RunsOnEverySample_WithoutError()
    {
        foreach (var def in Samples())
        {
            var candles = RampThroughLevel(def.Symbol, low: 0.30m, high: 6.00m, bars: 25);
            var report = StrategyBacktester.Run(def, candles);

            Assert.That(report, Is.Not.Null);
            Assert.That(report.Symbol, Is.EqualTo(def.Symbol));
            Assert.That(report.BarsProcessed, Is.EqualTo(candles.Count));
        }
    }

    [Test]
    public void Backtest_NciBreakoutTrigger_FiresWhenPriceCrossesLevel()
    {
        var nci = Strategies_20260217.NCI(); // Breakout(3.68)
        var start = new DateTime(2026, 5, 29, 13, 30, 0, DateTimeKind.Utc);

        Candle Bar(int m, decimal h, decimal l, decimal c) => new()
        {
            Symbol = "NCI", StartUtc = start.AddMinutes(m), EndUtc = start.AddMinutes(m + 1),
            Open = c, High = h, Low = l, Close = c, Volume = 5000m,
        };

        var candles = new List<Candle>
        {
            Bar(0, 3.40m, 3.20m, 3.30m),
            Bar(1, 3.60m, 3.30m, 3.55m),
            Bar(2, 3.85m, 3.50m, 3.80m),  // high 3.85 >= 3.68 -> Breakout fires
            Bar(3, 3.90m, 3.55m, 3.70m),
        };

        var report = StrategyBacktester.Run(nci, candles);

        Assert.That(report.Triggers.Any(t => t.Condition == "Breakout(3.68)"), Is.True,
                    "the breakout trigger should fire on the bar that crosses 3.68");
    }

    private static List<Candle> RampThroughLevel(string symbol, decimal low, decimal high, int bars)
    {
        var start = new DateTime(2026, 5, 29, 13, 30, 0, DateTimeKind.Utc);
        var step = (high - low) / Math.Max(1, bars - 1);
        var list = new List<Candle>(bars);
        for (int i = 0; i < bars; i++)
        {
            var price = low + step * i;
            list.Add(new Candle
            {
                Symbol = symbol,
                StartUtc = start.AddMinutes(i),
                EndUtc = start.AddMinutes(i + 1),
                Open = price,
                High = price + 0.05m,
                Low = price - 0.05m,
                Close = price,
                Volume = 5000m,
            });
        }
        return list;
    }
}
