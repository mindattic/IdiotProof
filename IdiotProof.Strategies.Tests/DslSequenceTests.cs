using System.Runtime.CompilerServices;
using System.Text;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Sequenced strategies (IP-T445): ordered Steps inside one canonical strategy. Every
/// pre-existing, single-step strategy must keep its exact JSON and stamp (IP-LAW-8/11);
/// the goldens in <c>Sequences/</c> were captured before steps existed and are the proof.
/// Regenerate only for an intentional canon change: <c>IDIOTPROOF_UPDATE_GOLDEN=1</c>.
/// </summary>
public class DslSequenceTests
{
    private static GapperProfile ClassicGapper() => new()
    {
        Id = "classic-gapper", Name = "Classic",
        MinGapPercent = 5, MaxGapPercent = 20, MinVolumeRatio = 2, MinPrice = 1, MaxPrice = 50,
        EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
        StopLossPercent = 5, TrailingStopPercent = 8, PeakGivebackPercent = 25,
        ArmExitAtEt = "09:15", SellByEt = "09:28", DefaultNotional = 1000m,
    };

    private static StrategyDefinition OneShare(StrategyDefinition def)
    {
        if (def.Quantity <= 0 && def.NotionalAmount is null) def.Quantity = 1;
        return def;
    }

    // The built-in sample catalog (strategy samples) plus the gapper canon, as they stood before steps.
    private static readonly (string Name, Func<StrategyDefinition> Make)[] PreExisting =
    [
        ("vwap-reclaim", () => Stock.Ticker("SPY").Name("SPY VWAP reclaim")
            .RequireAdxAbove(20).RequireEmaStack(9, 21).OnVwapReclaim().WithVolumeConfirm(1.2)
            .Quantity(1).Long().StopLossPercent(1.5).TakeProfitPercent(3).Build()),
        ("ema-pullback", () => Stock.Ticker("QQQ").Name("QQQ 9 EMA pullback")
            .IsAboveVwap().IsBetweenEma(9, 21).OnReclaim(9)
            .Quantity(1).Long().StopLossPercent(1).TrailingStopLoss(1.5).SellBy("15:45").Build()),
        ("opening-window", () => Stock.Ticker("AAPL").Name("AAPL first-hour breakout")
            .RequireEntryWindow("09:45", "10:30").Breakout(230).IsAboveVwap().WithVolumeConfirm(1.5)
            .Quantity(1).Long().StopLoss(227.5).TakeProfit(236).Build()),
        ("rsi-bounce", () => Stock.Ticker("AMD").Name("AMD oversold bounce")
            .IsRsiOversold(30).IsHammer().IsAtSupport(0.5)
            .Quantity(1).Long().StopLossPercent(2).TakeProfitPercent(4).PeakGiveback(40).Build()),
        ("nci-breakout-pullback", () => OneShare(Strategies_20260217.NCI())),
        ("erna-premarket", () => OneShare(Strategies_20260217.ERNA())),
        ("sune-wedge", () => OneShare(Strategies_20260217.SUNE())),
        ("gapper", () => GapperScriptFactory.Compose("GAPT", ClassicGapper()).Build()),
        ("velocity-gapper", () => GapperScriptFactory.Compose("VELO", ClassicGapper())
            .VelocityExhaustion(0.5, 3, "08:30").VelocityExhaustionMinRun(5).VelocityExhaustionRequireLowerHigh().Build()),
        ("branching", () => Stock.Ticker("NVDA").Name("VWAP fork").RequireEmaStack(9, 21)
            .IsAboveVwap().Then(b => b.Long().TakeProfit(152, 158).StopLossPercent(2))
            .ElseIf(c => c.IsBelowVwap(), b => b.Short().TakeProfit(141).StopLoss(149.5))
            .Else(b => b.Long().TakeProfit(150))
            .Quantity(25).StopLossPercent(3).SellBy("15:45").Build()),
        ("looped", () => Stock.Ticker("TST").While().IsPriceBetween(20, 100).EndWhile().ExitWhenFalse()
            .IsPriceBetween(1, 100).Long().StopLoss(5).ForEach(30, 31, 32, 33).TakeProfitEach().Repeat(2).Build()),
    ];

    private static IEnumerable<TestCaseData> PreExistingCases() =>
        PreExisting.Select(s => new TestCaseData(s.Name).SetName($"Golden_{s.Name}"));

    [TestCaseSource(nameof(PreExistingCases))]
    public void PreExistingStrategy_JsonAndStamp_AreByteIdentical(string name)
    {
        var def = PreExisting.Single(s => s.Name == name).Make();
        var json = StrategyJson.Serialize(def).Replace("\r\n", "\n");
        var stamp = def.ToStamp(new StampOptions { IncludePayload = false });
        var script = def.ToScript().Replace("\r\n", "\n");

        AssertGolden($"{name}.json", json);
        AssertGolden($"{name}.stamp.txt", stamp);
        AssertGolden($"{name}.script.txt", script);
    }

    private static void AssertGolden(string file, string actual)
    {
        var path = GoldenPath(file);
        if (Environment.GetEnvironmentVariable("IDIOTPROOF_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            return;
        }
        Assert.That(File.Exists(path), $"Missing golden {path} - run with IDIOTPROOF_UPDATE_GOLDEN=1 to create it.");
        var expected = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
        Assert.That(actual, Is.EqualTo(expected), $"{file} drifted: a non-sequence strategy must keep its exact canon.");
    }

    private static string GoldenPath(string file, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Sequences", file);

    // ── The two worked examples from the plan ───────────────────────────

    private static StrategyDefinition NvdaShortThenBuyBack() => Stock.Ticker("NVDA")
        .Step("short-into-earnings").Session(TradingSession.RTH).Quantity(50)
        .IsBelowVwap().IsRsiBearishDivergence().Short().StopLossPercent(3).TakeProfitPercent(6).SellBy("15:55")
        .ThenWhen(Conditions.HigherLows(3).And(Conditions.RequireEmaStack(9, 21)).And(Conditions.OnVwapReclaim))
        .Within(7)
        .Step("buy-back").Quantity(50).IsAboveVwap().IsEmaAbove(9).Long().StopLossPercent(2).TakeProfitPercent(5)
        .Build();

    private static StrategyDefinition GapperRideThenFade() => Stock.Ticker("GAPT")
        .Name("GAPT ride then fade").Session(TradingSession.Premarket)
        .Step("ride")
        .RequireEntryWindow("04:00", "09:00").IsGapBetween(5, 20).IsVolumeAbove(2).IsPriceBetween(1, 50)
        .Long().QuantityNotional(1000m).StopLossPercent(5).PeakGiveback(25, "09:35").SellBy("11:00")
        .ThenWhen(c => c.IsLowerHigh().And(c.IsBelowVwap()))
        .Within(1)
        .Step("fade")
        .RequireEntryWindow("09:35", "11:30").IsBelowVwap().VelocityBelow(0.2, 3)
        .Short().QuantityNotional(1000m).StopLossPercent(4).TakeProfitPercent(8).SellBy("15:55")
        .Repeat(2)
        .Build();

    [Test]
    public void Builder_StepRightAfterTicker_NamesTheRoot_AndLaterStepsLandInNextSteps()
    {
        var def = NvdaShortThenBuyBack();
        Assert.Multiple(() =>
        {
            Assert.That(def.IsSequence, Is.True);
            Assert.That(def.StepCount, Is.EqualTo(2));
            Assert.That(def.StepName, Is.EqualTo("short-into-earnings"));
            Assert.That(def.Direction, Is.EqualTo(TradeDirection.Short));
            Assert.That(def.EntryConditions.Select(c => c.ToScript()), Is.EqualTo(new[] { "IsBelowVwap()", "IsRsiBearishDivergence()" }));
            Assert.That(def.ThenWhen.Single().ToScript(), Is.EqualTo("HigherLows(3).And(RequireEmaStack(9, 21)).And(OnVwapReclaim())"));
            Assert.That(def.ThenWithinDays, Is.EqualTo(7));
            Assert.That(def.NextSteps, Has.Count.EqualTo(1));
            var buyBack = def.StepAt(1);
            Assert.That(buyBack.StepName, Is.EqualTo("buy-back"));
            Assert.That(buyBack.Symbol, Is.EqualTo("NVDA"));
            Assert.That(buyBack.Session, Is.EqualTo(TradingSession.RTH));
            Assert.That(buyBack.Direction, Is.EqualTo(TradeDirection.Long));
            Assert.That(buyBack.Quantity, Is.EqualTo(50));
            Assert.That(buyBack.StopLossPercent, Is.EqualTo(2));
            Assert.That(buyBack.ThenWhen, Is.Empty);
            Assert.That(buyBack.NextSteps, Is.Empty);
            Assert.That(def.StepAt(5), Is.SameAs(buyBack), "StepAt clamps");
            Assert.That(def.StepAt(-1), Is.SameAs(def));
            Assert.That(def.AllSteps().ToList(), Is.EqualTo(new[] { def, buyBack }));
        });
    }

    [Test]
    public void Builder_SessionAndRepeat_BelongToTheWholeSequence()
    {
        var def = GapperRideThenFade();
        Assert.Multiple(() =>
        {
            Assert.That(def.StepName, Is.EqualTo("ride"));
            Assert.That(def.Name, Is.EqualTo("GAPT ride then fade"));
            Assert.That(def.StepAt(1).Session, Is.EqualTo(TradingSession.Premarket), "later steps are seeded with the root session");
            Assert.That(def.ShouldRepeat, Is.True, "Repeat() inside a later step applies to the root");
            Assert.That(def.MaxCycles, Is.EqualTo(2));
            Assert.That(def.StepAt(1).ShouldRepeat, Is.False);
            Assert.That(def.StepAt(1).Direction, Is.EqualTo(TradeDirection.Short));
        });
    }

    [Test]
    public void Builder_StepAfterContent_OpensANewStep_EvenWhenTheRootIsUnnamed()
    {
        var def = Stock.Ticker("TST").IsAboveVwap().Long().StopLoss(1)
            .ThenWhen(c => c.IsBelowVwap())
            .Step("second").IsBelowVwap().Short().StopLoss(9).Build();
        Assert.Multiple(() =>
        {
            Assert.That(def.StepName, Is.Null);
            Assert.That(def.StepCount, Is.EqualTo(2));
            Assert.That(def.StepAt(1).StepName, Is.EqualTo("second"));
        });
    }

    [Test]
    public void Builder_StepClosesAnOpenWhile()
    {
        var def = Stock.Ticker("TST").While().IsAboveVwap().Long().StopLoss(1)
            .Step("two").IsBelowVwap().Short().StopLoss(9).Build();
        Assert.Multiple(() =>
        {
            Assert.That(def.WhileConditions, Has.Count.EqualTo(1));
            Assert.That(def.StepAt(1).WhileConditions, Is.Empty);
            Assert.That(def.StepAt(1).EntryConditions.Single().ToScript(), Is.EqualTo("IsBelowVwap()"));
        });
    }

    [Test]
    public void Builder_WithinRejectsZeroDays() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Stock.Ticker("TST").Within(0));

    // ── Text round trip ─────────────────────────────────────────────────

    [TestCase("nvda")]
    [TestCase("gapper")]
    public void ToScript_ThenParseScript_YieldsTheSameCanon(string which)
    {
        var def = which == "nvda" ? NvdaShortThenBuyBack() : GapperRideThenFade();
        var script = def.ToScript();
        var back = ScriptParser.ParseScript(script)!;
        Assert.Multiple(() =>
        {
            Assert.That(back.CanonicalJson(), Is.EqualTo(def.CanonicalJson()));
            Assert.That(back.ToScript(), Is.EqualTo(script));
            Assert.That(ScriptDiagnostics.Analyze(script), Is.Empty, script);
        });
    }

    [Test]
    public void ToScript_EmitsStepHeadersThenBodiesThenTransitions()
    {
        var script = NvdaShortThenBuyBack().ToScript();
        var lines = script.Split('\n').Select(l => l.Trim()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo("Ticker(\"NVDA\")"));
            Assert.That(lines[1], Is.EqualTo(".Step(\"short-into-earnings\")"));
            Assert.That(lines.IndexOf(".ThenWhen(HigherLows(3).And(RequireEmaStack(9, 21)).And(OnVwapReclaim()))"), Is.GreaterThan(lines.IndexOf(".Short()")));
            Assert.That(lines.IndexOf(".Within(7)"), Is.EqualTo(lines.IndexOf(".ThenWhen(HigherLows(3).And(RequireEmaStack(9, 21)).And(OnVwapReclaim()))") + 1));
            Assert.That(lines.IndexOf(".Step(\"buy-back\")"), Is.EqualTo(lines.IndexOf(".Within(7)") + 1));
            Assert.That(lines.Last(), Is.EqualTo(".StopLossPercent(2)"));
        });
    }

    [Test]
    public void ParseScript_HandAuthoredSequence_ReadsEveryStep()
    {
        var def = ScriptParser.ParseScript("""
            Ticker(TST)
                .Step("one").IsAboveVwap().Long().StopLoss(1)
                .ThenWhen(IsLowerHigh().And(IsBelowVwap())).Within(2)
                .Step("two").IsBelowVwap().Short().StopLoss(9)
                .ThenWhen(IsAboveVwap())
                .Step("three").IsAboveVwap().Long().StopLoss(1)
            """)!;
        Assert.Multiple(() =>
        {
            Assert.That(def.StepCount, Is.EqualTo(3));
            Assert.That(def.AllSteps().Select(s => s.StepName), Is.EqualTo(new[] { "one", "two", "three" }));
            Assert.That(def.ThenWhen.Single().ToScript(), Is.EqualTo("IsLowerHigh().And(IsBelowVwap())"));
            Assert.That(def.ThenWithinDays, Is.EqualTo(2));
            Assert.That(def.StepAt(1).ThenWhen.Single().ToScript(), Is.EqualTo("IsAboveVwap()"));
            Assert.That(def.StepAt(1).ThenWithinDays, Is.Null);
            Assert.That(def.StepAt(2).Direction, Is.EqualTo(TradeDirection.Long));
        });
    }

    [Test]
    public void VerbCatalog_ReflectsTheStepVerbs()
    {
        Assert.That(ScriptDiagnostics.KnownVerbs, Is.SupersetOf(new[] { "Step", "ThenWhen", "Within" }));
        Assert.That(ScriptDiagnostics.IsKnownVerb("thenwhen"), Is.True);
    }

    // ── Canonical JSON ──────────────────────────────────────────────────

    [TestCase("nvda")]
    [TestCase("gapper")]
    public void Json_RoundTrips_WithStepsAndTransitions(string which)
    {
        var def = which == "nvda" ? NvdaShortThenBuyBack() : GapperRideThenFade();
        var json = StrategyJson.Serialize(def);
        var back = StrategyJson.Deserialize(json);
        Assert.Multiple(() =>
        {
            Assert.That(back.CanonicalJson(), Is.EqualTo(def.CanonicalJson()));
            Assert.That(back.StepCount, Is.EqualTo(2));
            Assert.That(back.StepAt(1).StepName, Is.EqualTo(def.StepAt(1).StepName));
            Assert.That(back.ThenWhen.Select(c => c.ToScript()), Is.EqualTo(def.ThenWhen.Select(c => c.ToScript())));
            Assert.That(back.ThenWithinDays, Is.EqualTo(def.ThenWithinDays));
            Assert.That(back.ToScript(), Is.EqualTo(def.ToScript()));
        });
    }

    [Test]
    public void Json_SequenceKeys_AreWrittenOnlyWhereUsed()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(StrategyJson.Serialize(NvdaShortThenBuyBack()))!.AsObject();
        var step = node["nextSteps"]!.AsArray().Single()!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(node["schemaVersion"]!.GetValue<int>(), Is.EqualTo(StrategyJson.CurrentSchemaVersion), "schema version unchanged");
            Assert.That(node["stepName"]!.GetValue<string>(), Is.EqualTo("short-into-earnings"));
            Assert.That(node["thenWhen"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(node["thenWhen"]![0]!["type"]!.GetValue<string>(), Is.EqualTo("and"));
            Assert.That(node["thenWithinDays"]!.GetValue<int>(), Is.EqualTo(7));
            Assert.That(step["stepName"]!.GetValue<string>(), Is.EqualTo("buy-back"));
            Assert.That(step["symbol"]!.GetValue<string>(), Is.EqualTo("NVDA"));
            Assert.That(step.ContainsKey("schemaVersion"), Is.False);
            Assert.That(step.ContainsKey("nextSteps"), Is.False);
            Assert.That(step.ContainsKey("thenWhen"), Is.False);
            Assert.That(step.ContainsKey("thenWithinDays"), Is.False);
            Assert.That(node.Select(p => p.Key).Last(), Is.EqualTo("nextSteps"), "sequence keys trail every existing key");
        });
    }

    [Test]
    public void Json_FailsClosed_OnNestedSteps_ForeignSymbol_AndBadDeadline()
    {
        var json = StrategyJson.Serialize(NvdaShortThenBuyBack());
        var nested = json.Replace("\"stepName\": \"buy-back\"", "\"stepName\": \"buy-back\", \"nextSteps\": []");
        var foreign = new System.Text.RegularExpressions.Regex("\"symbol\": \"NVDA\"").Replace(json, "\"symbol\": \"AMD\"", 1);
        var badDays = json.Replace("\"thenWithinDays\": 7", "\"thenWithinDays\": 0");
        Assume.That(nested, Is.Not.EqualTo(json));
        Assume.That(foreign, Is.Not.EqualTo(json));
        Assume.That(badDays, Is.Not.EqualTo(json));
        Assert.Multiple(() =>
        {
            Assert.That(() => StrategyJson.Deserialize(nested), Throws.TypeOf<StrategyJsonException>().With.Message.Contains("nextSteps"));
            Assert.That(() => StrategyJson.Deserialize(foreign), Throws.TypeOf<StrategyJsonException>().With.Message.Contains("same symbol"));
            Assert.That(() => StrategyJson.Deserialize(badDays), Throws.TypeOf<StrategyJsonException>().With.Message.Contains("thenWithinDays"));
        });
    }

    // ── Stamp / HTML / fingerprint ──────────────────────────────────────

    [Test]
    public void Stamp_CoversEveryStep_AndTheFingerprintChangesWithTheSequence()
    {
        var def = GapperRideThenFade();
        var rootOnly = GapperRideThenFade();
        rootOnly.NextSteps.Clear();
        rootOnly.ThenWhen.Clear();
        rootOnly.ThenWithinDays = null;
        var stamp = def.ToStamp(new StampOptions { IncludePayload = false });

        Assert.Multiple(() =>
        {
            Assert.That(def.ShortId(), Is.Not.EqualTo(rootOnly.ShortId()), "the canon hash covers the steps");
            Assert.That(stamp, Does.Contain("STEP 1/2").And.Contain("STEP 2/2").And.Contain("THEN WHEN"));
            Assert.That(stamp, Does.Contain("STEPS    2 in sequence: ride > fade"));
            Assert.That(stamp, Does.Contain("lower high printed AND price below VWAP"));
            Assert.That(stamp, Does.Contain("within 1 day of the exit"));
            Assert.That(stamp, Does.Contain("SELL SHORT to open"));
            foreach (var line in stamp.TrimEnd('\n').Split('\n'))
            {
                Assert.That(line.Length, Is.LessThanOrEqualTo(StrategyStamp.Width), $"too wide: '{line}'");
                Assert.That(line, Does.Not.EndWith(" "));
            }
            Assert.That(StrategyStamp.Decode(def.ToStamp()).CanonicalJson(), Is.EqualTo(def.CanonicalJson()), "a pasted stamp recreates the sequence");
        });
    }

    [Test]
    public void Html_RendersAStepHeaderAndTransitionCardPerStep()
    {
        var html = NvdaShortThenBuyBack().ToHtml();
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("Step 1/2").And.Contain("Step 2/2").And.Contain("Then when"));
            Assert.That(html, Does.Contain("short-into-earnings").And.Contain("buy-back"));
            Assert.That(html, Does.Contain("7 days of the exit"));
            Assert.That(html.Split("data-phase=\"exit\"").Length - 1, Is.EqualTo(2), "one exit card per step");
        });
    }

    [Test]
    public void EmaPeriods_AreCollectedFromThenWhenAndLaterSteps()
    {
        var periods = EmaPeriodCollector.Collect(NvdaShortThenBuyBack());
        Assert.That(periods, Is.EquivalentTo(new[] { 9, 21 }));
    }

    [Test]
    public void BranchResolver_KeepsTheSequenceOnTheClone()
    {
        var def = Stock.Ticker("TST").IsAboveVwap().Then(b => b.StopLoss(5)).Else(b => b.StopLoss(9))
            .Long().ThenWhen(c => c.IsBelowVwap()).Within(3)
            .Step("two").IsBelowVwap().Short().StopLoss(9).Build();
        var resolved = StrategyBranchResolver.Resolve(def, new IdiotProof.Shared.IndicatorSnapshot { Price = 10 });
        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.Not.SameAs(def));
            Assert.That(resolved.ThenWhen, Has.Count.EqualTo(1));
            Assert.That(resolved.ThenWithinDays, Is.EqualTo(3));
            Assert.That(resolved.NextSteps.Single(), Is.SameAs(def.NextSteps.Single()));
        });
    }

    // ── Diagnostics ─────────────────────────────────────────────────────

    private static IReadOnlyList<ScriptDiagnostic> Diags(string text) => ScriptDiagnostics.Analyze(text);

    [Test]
    public void Diagnostics_ThenWhenWithoutAFollowingStep() =>
        Assert.That(Diags("Ticker(\"X\").IsAboveVwap().Long().StopLoss(1).ThenWhen(IsBelowVwap()).Within(2)").Select(d => d.Code),
            Is.EqualTo(new[] { "IPS025" }));

    [Test]
    public void Diagnostics_StepInsideIfOrWhile()
    {
        var insideIf = Diags("Ticker(\"X\").If(IsAboveVwap()).Then(Long()).Step(\"two\").Short().StopLoss(1).EndIf()");
        var insideWhile = Diags("Ticker(\"X\").Long().StopLoss(1).While().IsAboveVwap().Step(\"two\").Short().StopLoss(1).EndWhile()");
        Assert.Multiple(() =>
        {
            Assert.That(insideIf.Select(d => d.Code), Does.Contain("IPS021"));
            Assert.That(insideWhile.Select(d => d.Code), Does.Contain("IPS021"));
        });
    }

    [Test]
    public void Diagnostics_RepeatInsideAStep_IsAWarning()
    {
        var d = Diags("Ticker(\"X\").Long().StopLoss(1).ThenWhen(IsBelowVwap()).Step(\"two\").Short().StopLoss(1).Repeat()").Single();
        Assert.Multiple(() =>
        {
            Assert.That(d.Code, Is.EqualTo("IPS022"));
            Assert.That(d.IsError, Is.False);
        });
    }

    [Test]
    public void Diagnostics_DuplicateStepNames_AndEmptyStep()
    {
        var dup = Diags("Ticker(\"X\").Step(\"a\").Long().StopLoss(1).ThenWhen(IsBelowVwap()).Step(\"a\").Short().StopLoss(1)");
        var empty = Diags("Ticker(\"X\").Long().StopLoss(1).ThenWhen(IsBelowVwap()).Step(\"two\").StopLoss(1)");
        Assert.Multiple(() =>
        {
            Assert.That(dup.Select(d => d.Code), Is.EqualTo(new[] { "IPS023" }));
            Assert.That(empty.Select(d => d.Code), Is.EqualTo(new[] { "IPS024" }));
            Assert.That(empty[0].Message, Does.Contain("\"two\""));
        });
    }

    [Test]
    public void Diagnostics_ThenWhenNeedsAReadableCondition()
    {
        var bare = Diags("Ticker(\"X\").Long().StopLoss(1).ThenWhen().Step(\"two\").Short().StopLoss(1)");
        Assert.That(bare.Select(d => d.Code), Is.EqualTo(new[] { "IPS020" }));
    }

    [Test]
    public void Diagnostics_WellFormedSequences_AreClean()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Diags(NvdaShortThenBuyBack().ToScript()), Is.Empty);
            Assert.That(Diags(GapperRideThenFade().ToScript()), Is.Empty);
            Assert.That(Diags("Ticker(\"X\").Name(\"n\").Quantity(2).Step(\"a\").Long().StopLoss(1).ThenWhen(IsBelowVwap()).Step(\"b\").Short().StopLoss(1)"), Is.Empty);
        });
    }
}
