using IdiotProof.Core.Data;
using IdiotProof.Models;
using IdiotProof.Operations.Monitor;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Operations.Tests.Monitor;

/// <summary>tick-once evaluates leader guards against the leader readings it was given (IP-T395).</summary>
public class TickVerdictLeaderTests
{
    // Monday 2026-06-01 14:00 UTC = 10:00 ET, inside RTH.
    private static readonly DateTime Now = new(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc);

    private static List<Candle> Bars() =>
        Enumerable.Range(0, 5).Select(i => new Candle
        {
            Symbol = "FOLL", StartUtc = Now.AddMinutes(i - 5), EndUtc = Now.AddMinutes(i - 4),
            Open = 2.5m, High = 2.5m, Low = 2.5m, Close = 2.5m, Volume = 1000,
        }).ToList();

    private static Strategy Row()
    {
        var def = Stock.Ticker("FOLL").LeaderConfirms(1, 0.2).Long().StopLoss(1).Build();
        return new Strategy { Id = Guid.NewGuid(), Symbol = "FOLL", Title = "leader test", ScriptText = def.ToScript(), ScriptJson = StrategyJson.Serialize(def) };
    }

    [Test]
    public void LeaderMovedUp_WouldFire_FallingOrUnknown_Waits()
    {
        var up = MonitorTickOnceOperation.Evaluate(Row(), Bars(), null, Now, [new LeaderReading("LEAD", 1, 0.6, 0.01)]);
        var down = MonitorTickOnceOperation.Evaluate(Row(), Bars(), null, Now, [new LeaderReading("LEAD", 1, 0.6, -0.01)]);
        var unknown = MonitorTickOnceOperation.Evaluate(Row(), Bars(), null, Now);

        Assert.Multiple(() =>
        {
            Assert.That(up.Verdict, Is.EqualTo("would-fire"));
            Assert.That(down.Verdict, Is.EqualTo("waiting"));
            Assert.That(unknown.Verdict, Is.EqualTo("waiting"), "no leader context fails closed");
        });
    }
}
