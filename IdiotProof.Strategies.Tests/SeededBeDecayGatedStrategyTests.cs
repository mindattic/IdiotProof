using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Pins the exact ScriptJson seeded by tools/seed-be-decay-gated-strategy.sql
/// (the "BE - Pullback &amp; EMA Reclaim (BEX decay-gated)" strategy). Without this,
/// a typo in the hand-written JSON would only surface at runtime as a silent
/// MonitorWorker quarantine (StrategyLoader.Load's CanonicalError path) instead
/// of a build-time failure.
/// </summary>
public class SeededBeDecayGatedStrategyTests
{
    private const string SeededJson = """
    {
      "schemaVersion": 1,
      "symbol": "BE",
      "name": "BE - Pullback & EMA Reclaim (BEX decay-gated)",
      "session": "RTH",
      "quantity": 1,
      "notionalAmount": null,
      "direction": "Long",
      "entryConditions": [
        { "type": "indicator", "indicator": "EmaStack", "p1": 9, "p2": 31, "phase": "Filters" },
        { "type": "indicator", "indicator": "ReclaimEma", "p1": 9 },
        { "type": "indicator", "indicator": "VolumeAbove", "p1": 1.2 },
        { "type": "indicator", "indicator": "VwapAbove" }
      ],
      "peakGivebackPercent": 25,
      "peakGivebackArmTime": "10:00",
      "stopLossPercent": 6,
      "exitTime": "15:55"
    }
    """;

    [Test]
    public void Deserializes_WithoutThrowing_AndPreservesEveryField()
    {
        var def = StrategyJson.Deserialize(SeededJson);

        Assert.Multiple(() =>
        {
            Assert.That(def.Symbol, Is.EqualTo("BE"));
            Assert.That(def.Session, Is.EqualTo(TradingSession.RTH));
            Assert.That(def.Quantity, Is.EqualTo(1));
            Assert.That(def.NotionalAmount, Is.Null);
            Assert.That(def.Direction, Is.EqualTo(TradeDirection.Long));
            Assert.That(def.EntryConditions, Has.Count.EqualTo(4));
            Assert.That(def.PeakGivebackPercent, Is.EqualTo(25));
            Assert.That(def.PeakGivebackArmTime, Is.EqualTo(new TimeSpan(10, 0, 0)));
            Assert.That(def.StopLossPercent, Is.EqualTo(6));
            Assert.That(def.ExitTime, Is.EqualTo(new TimeSpan(15, 55, 0)));
        });
    }

    [Test]
    public void RoundTrips_ThroughSerializeDeserialize()
    {
        var def = StrategyJson.Deserialize(SeededJson);
        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));

        Assert.That(restored.EntryConditions.Select(c => c.ToScript()),
            Is.EqualTo(def.EntryConditions.Select(c => c.ToScript())),
            "every condition survives a full serialize/deserialize round trip with identical semantics");
    }
}
