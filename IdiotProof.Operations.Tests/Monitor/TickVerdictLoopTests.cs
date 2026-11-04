using IdiotProof.Core.Data;
using IdiotProof.Models;
using IdiotProof.Operations.Monitor;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Monitor;

/// <summary>Gate 1 honors Repeat(n) and While() exactly as MonitorWorker walks it (IP-T370).</summary>
public class TickVerdictLoopTests
{
    // Monday 2026-06-01 14:00 UTC = 10:00 ET, inside RTH.
    private static readonly DateTime Now = new(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc);

    private static List<Candle> Bars(decimal price) =>
        Enumerable.Range(0, 5).Select(i => new Candle
        {
            Symbol = "TST",
            StartUtc = Now.AddMinutes(i - 5),
            EndUtc = Now.AddMinutes(i - 4),
            Open = price, High = price, Low = price, Close = price, Volume = 1000,
        }).ToList();

    private static Strategy Row(StrategyDefinition def, int cyclesToday = 0, string? cycleDateEt = null) => new()
    {
        Id = Guid.NewGuid(),
        Symbol = def.Symbol,
        Title = "loop test",
        ScriptText = def.ToScript(),
        ScriptJson = StrategyJson.Serialize(def),
        CyclesToday = cyclesToday,
        CycleDateEt = cycleDateEt,
    };

    private static StrategyDefinition RepeatTwice() =>
        Stock.Ticker("TST").IsPriceBetween(1, 100).Long().StopLoss(1).Repeat(2).Build();

    [Test]
    public void RepeatN_UsedUpToday_IsDoneForToday()
    {
        var verdict = MonitorTickOnceOperation.Evaluate(Row(RepeatTwice(), 2, "2026-06-01"), Bars(10), null, Now);
        Assert.That(verdict.Verdict, Is.EqualTo("done-today"));
        Assert.That(verdict.Detail, Does.Contain("Repeat(2)"));
    }

    [Test]
    public void RepeatN_WithCyclesLeft_StillFires()
    {
        var verdict = MonitorTickOnceOperation.Evaluate(Row(RepeatTwice(), 1, "2026-06-01"), Bars(10), null, Now);
        Assert.That(verdict.Verdict, Is.EqualTo("would-fire"));
    }

    [Test]
    public void RepeatN_YesterdaysCount_DoesNotGateToday()
    {
        var verdict = MonitorTickOnceOperation.Evaluate(Row(RepeatTwice(), 2, "2026-05-29"), Bars(10), null, Now);
        Assert.That(verdict.Verdict, Is.EqualTo("would-fire"));
    }

    [Test]
    public void While_False_PausesAndNamesTheGuard()
    {
        var def = Stock.Ticker("TST").While().IsPriceBetween(20, 100).EndWhile()
            .IsPriceBetween(1, 100).Long().StopLoss(1).Build();

        var paused = MonitorTickOnceOperation.Evaluate(Row(def), Bars(10), null, Now);
        var armed = MonitorTickOnceOperation.Evaluate(Row(def), Bars(25), null, Now);

        Assert.Multiple(() =>
        {
            Assert.That(paused.Verdict, Is.EqualTo("paused"));
            Assert.That(paused.Detail, Does.Contain("IsPriceBetween(20, 100)"));
            Assert.That(armed.Verdict, Is.EqualTo("would-fire"));
        });
    }
}
