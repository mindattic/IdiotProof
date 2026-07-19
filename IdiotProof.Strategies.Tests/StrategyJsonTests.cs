using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// The canonical strategy format (IP-A13 / IP-LAW-8): a versioned, STRICT
/// JSON round trip of the semantic model. Two guarantees under test:
/// (1) lossless round trip — including composed conditions and branching,
/// which the text round trip historically dropped; (2) fail-closed reads —
/// unknown versions/types/properties throw instead of partially evaluating.
/// </summary>
public class StrategyJsonTests
{
    private static GapperProfile Profile() => new()
    {
        Id = "classic-gapper", Name = "Classic",
        MinGapPercent = 5, MaxGapPercent = 20, MinVolumeRatio = 2, MinPrice = 1, MaxPrice = 50,
        EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
        StopLossPercent = 5, TrailingStopPercent = 8, PeakGivebackPercent = 25,
        ArmExitAtEt = "09:15", SellByEt = "09:28", DefaultNotional = 1000m,
    };

    [Test]
    public void RoundTrip_Gapper_PreservesEveryField()
    {
        var original = GapperScriptFactory.Compose("GAPT", Profile()).Build();

        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(original));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Symbol, Is.EqualTo("GAPT"));
            Assert.That(restored.Name, Is.EqualTo(original.Name));
            Assert.That(restored.Session, Is.EqualTo(TradingSession.Premarket));
            Assert.That(restored.NotionalAmount, Is.EqualTo(1000m));
            Assert.That(restored.Direction, Is.EqualTo(TradeDirection.Long));
            Assert.That(restored.StopLossPercent, Is.EqualTo(5));
            Assert.That(restored.TrailingStopPercent, Is.EqualTo(8));
            Assert.That(restored.PeakGivebackPercent, Is.EqualTo(25));
            Assert.That(restored.PeakGivebackArmTime, Is.EqualTo(new TimeSpan(9, 15, 0)));
            Assert.That(restored.ExitTime, Is.EqualTo(new TimeSpan(9, 28, 0)));
            Assert.That(restored.EntryConditions.Select(c => c.ToScript()),
                Is.EqualTo(original.EntryConditions.Select(c => c.ToScript())),
                "every condition survives with identical semantics");
        });
    }

    [Test]
    public void RoundTrip_ComposedConditionsAndBranching_SurviveWhereTextDoesNot()
    {
        // .And()/.Not() composition and .Then()/.Else() branching are exactly
        // what the regex text parser cannot round-trip — the canon must.
        var def = Stock.Ticker("NVDA")
            .Name("Branchy")
            .RequireAdxAbove(20)
            .Build();
        def.EntryConditions.Add(
            Conditions.IsAboveVwap.And(Conditions.IsAboveEma(9)).Or(Conditions.IsGapUp(5).Not()));

        var block = new ConditionalBlock();
        block.Branches.Add(new ConditionalBranch
        {
            Condition = Conditions.IsAboveVwap,
            Overrides = new StrategyOverrides { Direction = TradeDirection.Long, TakeProfitPrice = 12, StopLossPercent = 3 },
        });
        block.Branches.Add(new ConditionalBranch { Condition = null, Overrides = new StrategyOverrides { Direction = TradeDirection.Short } });
        def.ConditionalBlocks.Add(block);

        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));

        Assert.Multiple(() =>
        {
            Assert.That(restored.EntryConditions.Select(c => c.ToScript()),
                Is.EqualTo(def.EntryConditions.Select(c => c.ToScript())),
                "And/Or/Not composition is preserved structurally");
            Assert.That(restored.ConditionalBlocks, Has.Count.EqualTo(1));
            Assert.That(restored.ConditionalBlocks[0].Branches, Has.Count.EqualTo(2));
            Assert.That(restored.ConditionalBlocks[0].Branches[0].Overrides.TakeProfitPrice, Is.EqualTo(12));
            Assert.That(restored.ConditionalBlocks[0].Branches[0].Overrides.StopLossPercent, Is.EqualTo(3));
            Assert.That(restored.ConditionalBlocks[0].Branches[1].Condition, Is.Null, "else branch survives");
            Assert.That(restored.ConditionalBlocks[0].Branches[1].Overrides.Direction, Is.EqualTo(TradeDirection.Short));
        });
    }

    [Test]
    public void Deserialize_UnknownSchemaVersion_Throws()
    {
        var json = StrategyJson.Serialize(Stock.Ticker("TST").Build())
            .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
        Assert.That(() => StrategyJson.Deserialize(json),
            Throws.TypeOf<StrategyJsonException>().With.Message.Contains("schemaVersion"));
    }

    [Test]
    public void Deserialize_UnknownConditionType_Throws()
    {
        const string json = """
        { "schemaVersion": 1, "symbol": "TST",
          "entryConditions": [ { "type": "quantumVibes", "level": 9000 } ] }
        """;
        Assert.That(() => StrategyJson.Deserialize(json),
            Throws.TypeOf<StrategyJsonException>().With.Message.Contains("quantumVibes"));
    }

    [Test]
    public void Deserialize_UnknownProperty_Throws()
    {
        // Must-understand, not must-ignore: a field this build doesn't know
        // could be a gate written by a newer build — refusing beats dropping.
        const string json = """
        { "schemaVersion": 1, "symbol": "TST", "futureRiskGate": true }
        """;
        Assert.That(() => StrategyJson.Deserialize(json),
            Throws.TypeOf<StrategyJsonException>().With.Message.Contains("futureRiskGate"));
    }

    [Test]
    public void Deserialize_Garbage_Throws()
    {
        Assert.That(() => StrategyJson.Deserialize("not json"), Throws.TypeOf<StrategyJsonException>());
    }

    [Test]
    public void Deserialize_UnrepresentableDecimal_QuarantinesInsteadOfCrashing()
    {
        // 1e30 overflows decimal. This must surface as StrategyJsonException
        // (→ quarantine) — a raw FormatException would escape StrategyLoader's
        // net and crash the Monitor's evaluation instead of quarantining the row.
        const string json = """
        { "schemaVersion": 1, "symbol": "TST", "notionalAmount": 1e30 }
        """;
        Assert.That(() => StrategyJson.Deserialize(json),
            Throws.TypeOf<StrategyJsonException>().With.Message.Contains("notionalAmount"));
    }

    [Test]
    public void Deserialize_OverflowingDouble_Throws_NotSilentInfinity()
    {
        // 1e400 parses to double.PositiveInfinity on modern .NET — an Infinity
        // stop percent must never be accepted as a "understood" value.
        const string json = """
        { "schemaVersion": 1, "symbol": "TST", "stopLossPercent": 1e400 }
        """;
        Assert.That(() => StrategyJson.Deserialize(json),
            Throws.TypeOf<StrategyJsonException>().With.Message.Contains("stopLossPercent"));
    }

    [Test]
    public void Deserialize_WrongKindValue_Throws_NotSilentDefault()
    {
        // "session": 5 used to silently coerce to the RTH default — a canon
        // field the reader doesn't FULLY understand must fail closed (IP-LAW-8).
        const string json = """
        { "schemaVersion": 1, "symbol": "TST", "session": 5 }
        """;
        Assert.That(() => StrategyJson.Deserialize(json),
            Throws.TypeOf<StrategyJsonException>().With.Message.Contains("session"));
    }

    [Test]
    public void Loader_PresentButBrokenCanon_QuarantinesInsteadOfTextFallback()
    {
        // The text says "buy anything" — the canon is broken. The loader must
        // NOT quietly run the text: quarantine with the canon's error.
        var result = StrategyLoader.Load(
            scriptJson: """{ "schemaVersion": 1, "symbol": "TST", "mystery": 1 }""",
            scriptText: "Ticker(\"TST\")\n    .Long()");

        Assert.Multiple(() =>
        {
            Assert.That(result.Definition, Is.Null);
            Assert.That(result.CanonicalError, Does.Contain("mystery"));
        });
    }

    [Test]
    public void Loader_LegacyRowWithoutCanon_FallsBackToTextParse()
    {
        var result = StrategyLoader.Load(scriptJson: null,
            scriptText: "Ticker(\"TST\")\n    .IsGapUp(5)\n    .Long()");

        Assert.Multiple(() =>
        {
            Assert.That(result.Definition, Is.Not.Null);
            Assert.That(result.FromCanonicalJson, Is.False);
            Assert.That(result.CanonicalError, Is.Null);
            Assert.That(result.Definition!.Symbol, Is.EqualTo("TST"));
        });
    }

    [Test]
    public void Loader_ValidCanon_WinsOverText()
    {
        // Text and canon disagree; the canon is what runs.
        var canonical = StrategyJson.Serialize(Stock.Ticker("REAL").IsGapUp(7).Long().Build());
        var result = StrategyLoader.Load(canonical, "Ticker(\"STALE\")\n    .Short()");

        Assert.Multiple(() =>
        {
            Assert.That(result.FromCanonicalJson, Is.True);
            Assert.That(result.Definition!.Symbol, Is.EqualTo("REAL"));
            Assert.That(result.Definition.Direction, Is.EqualTo(TradeDirection.Long));
        });
    }
}
