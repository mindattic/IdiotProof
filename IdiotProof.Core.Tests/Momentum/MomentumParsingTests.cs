using IdiotProof.Core.Research.Momentum;

namespace IdiotProof.Core.Tests.Momentum;

/// <summary>
/// IP-T493 "Momentum." claim tracker: the spoken-price scale resolver, the spelled-letter ticker parser and the
/// scenario evaluator. Pure functions, no database or network.
/// </summary>
[TestFixture]
public sealed class SpokenPriceTests
{
    [TestCase("270", 2.40, 2.70)]
    [TestCase("375", 3.10, 3.75)]
    [TestCase("250", 3.26, 2.50)]
    [TestCase("445", 3.26, 4.45)]
    [TestCase("750", 3.04, 7.50)]
    [TestCase("1250", 11.80, 12.50)]
    [TestCase("56", 3.04, 5.60)]
    [TestCase("53", 0.54, 0.53)]
    [TestCase("80", 0.54, 0.80)]
    [TestCase("19", 13.35, 19.00)]
    [TestCase("33", 0.45, 0.33)]
    [TestCase("$19", 13.35, 19.00)]
    [TestCase("previous high of the day over $19 a share", 13.35, 19.00)]
    [TestCase("daily 200 moving average at $30 a share", 13.35, 30.00)]
    public void BareNumber_ScalesToTheReferencePrice(string spoken, double reference, double expected)
    {
        var r = SpokenPrice.Resolve(spoken, (decimal)reference);
        Assert.That(r, Is.Not.Null);
        Assert.That(r!.Price, Is.EqualTo((decimal)expected));
    }

    [TestCase("$3.50", 3.50)]
    [TestCase("$2.70", 2.70)]
    [TestCase("1.50", 1.50)]
    [TestCase("five bucks", 5.00)]
    [TestCase("10 bucks", 10.00)]
    [TestCase("30 bucks a share", 30.00)]
    [TestCase("60 cents", 0.60)]
    [TestCase("90 cents", 0.90)]
    [TestCase("a dollar", 1.00)]
    [TestCase("a dollar 78", 1.78)]
    [TestCase("a dollar and 60 cents", 1.60)]
    [TestCase("a dollar 90 cent", 1.90)]
    [TestCase("a dollar fifty", 1.50)]
    [TestCase("a dollar thirty-five", 1.35)]
    [TestCase("one eighty eight", 1.88)]
    [TestCase("one seventy five", 1.75)]
    [TestCase("two fifty", 2.50)]
    [TestCase("four", 4.00)]
    public void LiteralForms_AreTakenAsSaid_WhateverTheReference(string spoken, double expected)
    {
        // a reference far away proves the literal forms ignore it
        var r = SpokenPrice.Resolve(spoken, 0.25m);
        Assert.That(r, Is.Not.Null);
        Assert.That(r!.Price, Is.EqualTo((decimal)expected));
    }

    [Test]
    public void DollarSign_OnABareInteger_StillScalesWhenFarFromThePrice()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpokenPrice.Resolve("$270", 2.40m)!.Price, Is.EqualTo(2.70m));
            Assert.That(SpokenPrice.Resolve("$5", 3.00m)!.Price, Is.EqualTo(5.00m));
            Assert.That(SpokenPrice.Resolve("$10", 3.00m)!.Price, Is.EqualTo(10.00m));
        });
    }

    [Test]
    public void BareNumber_WithoutAReference_IsNotGuessed()
    {
        Assert.That(SpokenPrice.Resolve("270"), Is.Null);
        Assert.That(SpokenPrice.Resolve("four")!.Price, Is.EqualTo(4m), "a single-digit word is dollars without a reference");
    }

    [Test]
    public void SecondReference_IsUsedWhenCloser()
    {
        // prior close $0.95 but the stock opened at $2.60: "270" is $2.70, not $0.27
        Assert.That(SpokenPrice.Resolve("270", 0.95m, 2.60m)!.Price, Is.EqualTo(2.70m));
    }

    [TestCase("VWAP", "vwap")]
    [TestCase("v web", "vwap")]
    [TestCase("Friday high of day", "prior-day-high")]
    [TestCase("the premarket high", "premarket-high")]
    [TestCase("new highs", "high-of-day")]
    [TestCase("yesterday's close", "prior-close")]
    public void ChartWords_AreKeywords_NotPrices(string spoken, string keyword)
    {
        Assert.That(SpokenPrice.Keyword(spoken), Is.EqualTo(keyword));
        Assert.That(SpokenPrice.Resolve(spoken, 3m), Is.Null);
    }

    [TestCase("270")]
    [TestCase("a dollar 50 VWAP")]
    [TestCase("$3.50")]
    public void Numbers_AreNotKeywords(string spoken) => Assert.That(SpokenPrice.Keyword(spoken), Is.Null);
}

[TestFixture]
public sealed class SpelledTickerTests
{
    [Test]
    public void FindSpelled_JoinsSpaceDotAndHyphenSeparatedLetters()
    {
        var text = "last week's watch list gave you one of the cleanest runners over on P A R A that squeezed. " +
                   "Then B.R.A.I. and S-E-G-G and C H O W too.";
        Assert.That(SpelledTicker.FindSpelled(text), Is.EqualTo(new[] { "PARA", "BRAI", "SEGG", "CHOW" }));
    }

    [Test]
    public void FindSpelled_IgnoresWordsAndSingleLetters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpelledTicker.FindSpelled("I want to watch VSA for a break of 270."), Is.Empty);
            Assert.That(SpelledTicker.FindSpelled("money flows into U S small caps"), Is.Empty, "U S is not a ticker");
            Assert.That(SpelledTicker.FindSpelled(""), Is.Empty);
            Assert.That(SpelledTicker.FindSpelled(null), Is.Empty);
        });
    }

    [Test]
    public void FindSpelled_ReportsEachTickerOnce()
    {
        Assert.That(SpelledTicker.FindSpelled("P A R A ran, and P A R A is still on watch"), Is.EqualTo(new[] { "PARA" }));
    }

    [TestCase("P A R A", "PARA")]
    [TestCase("P.A.R.A.", "PARA")]
    [TestCase("p-a-r-a", "PARA")]
    [TestCase("$chsn", "CHSN")]
    [TestCase(" VSA ", "VSA")]
    [TestCase("BRK.B", "BRK.B")]
    public void Normalize_ReturnsACandidateSymbol(string spoken, string expected) =>
        Assert.That(SpelledTicker.Normalize(spoken), Is.EqualTo(expected));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("the one that closed up 80 percent")]
    [TestCase("123")]
    public void Normalize_ReturnsNull_WhenNothingTickerShapedIsLeft(string? spoken) =>
        Assert.That(SpelledTicker.Normalize(spoken), Is.Null);

    [Test]
    public void Variants_FindOneEditNeighboursAmongKnownSymbols()
    {
        var known = new[] { "CHSN", "ONEG", "DKI", "VSA", "AAPL", "CH" };
        Assert.Multiple(() =>
        {
            Assert.That(SpelledTicker.Variants("CHS", known), Does.Contain("CHSN"), "letter added at the end");
            Assert.That(SpelledTicker.Variants("OEG", known), Does.Contain("ONEG"), "letter added in the middle");
            Assert.That(SpelledTicker.Variants("DKY", known), Does.Contain("DKI"), "letter swapped");
            Assert.That(SpelledTicker.Variants("CHS", known), Does.Contain("CH"), "letter dropped");
            Assert.That(SpelledTicker.Variants("VSA", known), Does.Not.Contain("VSA"), "the candidate itself is not a variant");
            Assert.That(SpelledTicker.Variants("CHS", known), Does.Not.Contain("AAPL"));
        });
    }
}

[TestFixture]
public sealed class MomentumScenarioEvaluatorTests
{
    private static readonly DateOnly Day = new(2026, 11, 6);

    /// <summary>Bars from (ET time, open, high, low, close); 1,000 shares each.</summary>
    private static IReadOnlyList<ScenarioBar> Bars(params (string Et, decimal O, decimal H, decimal L, decimal C)[] rows) =>
        MomentumScenarioEvaluator.WithVwap(rows.Select(r =>
        {
            var et = TimeSpan.Parse(r.Et, System.Globalization.CultureInfo.InvariantCulture);
            var utc = Day.ToDateTime(TimeOnly.FromTimeSpan(et)).AddHours(5); // EST
            return (DateTime.SpecifyKind(utc, DateTimeKind.Utc), et, r.O, r.H, r.L, r.C, 1000m);
        }));

    private static ScenarioSpec Break(decimal trigger, decimal[] targets, decimal[]? confirm = null, decimal? invalidation = null, bool requiresConfirm = false) =>
        new("break", new ScenarioLevel(trigger), (confirm ?? []).Select(c => new ScenarioLevel(c)).ToList(), targets, invalidation, requiresConfirm);

    [Test]
    public void NoBars_IsUnverifiable()
    {
        var r = MomentumScenarioEvaluator.Evaluate(Break(2.70m, [4m]), []);
        Assert.That(r.Verdict, Is.EqualTo(MomentumVerdicts.Unverifiable));
    }

    [Test]
    public void NeverTradesOverTheTrigger_IsNotTriggered()
    {
        var bars = Bars(("09:30", 2.50m, 2.60m, 2.45m, 2.55m), ("09:31", 2.55m, 2.65m, 2.50m, 2.60m));
        var r = MomentumScenarioEvaluator.Evaluate(Break(2.70m, [4m]), bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.Verdict, Is.EqualTo(MomentumVerdicts.NotTriggered));
            Assert.That(r.TriggerHit, Is.False);
            Assert.That(r.PlanPnlPct, Is.Null);
        });
    }

    [Test]
    public void BreakThenFirstTarget_ExitsAtTheTarget()
    {
        var bars = Bars(
            ("09:30", 2.60m, 2.68m, 2.58m, 2.66m),
            ("09:31", 2.66m, 2.80m, 2.65m, 2.78m),   // breaks 2.70: entry at the level
            ("09:32", 2.78m, 3.10m, 2.76m, 3.05m),   // reaches the 3.00 target
            ("09:33", 3.05m, 3.20m, 3.00m, 3.15m));
        var r = MomentumScenarioEvaluator.Evaluate(Break(2.70m, [3.00m, 4.00m]), bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.Verdict, Is.EqualTo(MomentumVerdicts.TargetHit));
            Assert.That(r.TriggerPrice, Is.EqualTo(2.70m));
            Assert.That(r.EntryPrice, Is.EqualTo(2.70m));
            Assert.That(r.ExitReason, Is.EqualTo("target"));
            Assert.That(r.ExitPrice, Is.EqualTo(3.00m));
            Assert.That(r.PlanPnlPct, Is.EqualTo(11.11).Within(0.01));
            Assert.That(r.TargetsHit, Is.EqualTo(1));
            Assert.That(r.FirstTargetUtc, Is.EqualTo(bars[2].StartUtc));
            Assert.That(r.MaxRunAfterTriggerPct, Is.EqualTo(18.52).Within(0.01));
        });
    }

    [Test]
    public void BreakThenCloseUnderTheStatedInvalidation_IsStopped()
    {
        var bars = Bars(
            ("09:30", 2.60m, 2.75m, 2.58m, 2.72m),   // trigger
            ("09:31", 2.72m, 2.74m, 2.40m, 2.42m),   // close 2.42 < 2.50 x 0.98
            ("09:32", 2.42m, 3.20m, 2.40m, 3.10m));  // the target comes later
        var r = MomentumScenarioEvaluator.Evaluate(Break(2.70m, [3.00m], invalidation: 2.50m), bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.Verdict, Is.EqualTo(MomentumVerdicts.Stopped));
            Assert.That(r.ExitReason, Is.EqualTo("stop"));
            Assert.That(r.ExitPrice, Is.EqualTo(2.42m));
            Assert.That(r.InvalidationSource, Is.EqualTo("stated"));
            Assert.That(r.InvalidationHitBeforeTarget, Is.True);
            Assert.That(r.AnyTargetHit, Is.True, "the target still counts as reached after the stop");
        });
    }

    [Test]
    public void ACloseJustUnderTheLevel_IsInsideTheStopBuffer()
    {
        var bars = Bars(
            ("09:30", 2.60m, 2.75m, 2.58m, 2.72m),
            ("09:31", 2.72m, 2.73m, 2.48m, 2.49m),   // 2.49 is above 2.50 x 0.98 = 2.45
            ("15:55", 2.49m, 2.60m, 2.48m, 2.55m));
        var r = MomentumScenarioEvaluator.Evaluate(Break(2.70m, [3.00m], invalidation: 2.50m), bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.ExitReason, Is.EqualTo("time"));
            Assert.That(r.ExitPrice, Is.EqualTo(2.55m));
            Assert.That(r.Verdict, Is.EqualTo(MomentumVerdicts.TriggeredNoTarget));
        });
    }

    [Test]
    public void RequiredPullbackConfirm_EntersAtTheConfirmBarClose()
    {
        var bars = Bars(
            ("09:30", 2.60m, 2.80m, 2.58m, 2.78m),   // break 2.70
            ("09:31", 2.78m, 2.79m, 2.52m, 2.56m),   // pulls to within 2% of 2.50 and closes over it: entry 2.56
            ("09:32", 2.56m, 3.05m, 2.55m, 3.00m));  // first target 3.00
        var r = MomentumScenarioEvaluator.Evaluate(Break(2.70m, [3.00m], confirm: [2.50m], requiresConfirm: true), bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.ConfirmHit, Is.True);
            Assert.That(r.ConfirmLevel, Is.EqualTo(2.50m));
            Assert.That(r.EntryPrice, Is.EqualTo(2.56m));
            Assert.That(r.ExitReason, Is.EqualTo("target"));
            Assert.That(r.PlanPnlPct, Is.EqualTo(17.19).Within(0.01));
            Assert.That(r.InvalidationSource, Is.EqualTo("default-confirm"));
            Assert.That(r.Verdict, Is.EqualTo(MomentumVerdicts.TargetHit));
        });
    }

    [Test]
    public void RequiredConfirmThatNeverComes_HasNoEntryAndNoPnl()
    {
        var bars = Bars(
            ("09:30", 2.60m, 2.80m, 2.58m, 2.78m),
            ("09:31", 2.78m, 3.10m, 2.77m, 3.05m));  // runs straight to target with no pullback
        var r = MomentumScenarioEvaluator.Evaluate(Break(2.70m, [3.00m], confirm: [2.50m], requiresConfirm: true), bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.TriggerHit, Is.True);
            Assert.That(r.ConfirmHit, Is.False);
            Assert.That(r.EntryPrice, Is.Null);
            Assert.That(r.PlanPnlPct, Is.Null);
            Assert.That(r.ExitReason, Is.EqualTo("no-confirm"));
            Assert.That(r.Verdict, Is.EqualTo(MomentumVerdicts.TargetHit), "the call still reached its target");
        });
    }

    [Test]
    public void APullbackAfterTheFirstTarget_IsNotThePlansEntry()
    {
        var bars = Bars(
            ("09:30", 2.60m, 2.80m, 2.58m, 2.78m),   // break 2.70
            ("09:31", 2.78m, 3.20m, 2.77m, 3.10m),   // first target 3.00 before any pullback
            ("09:32", 3.10m, 3.11m, 2.52m, 2.56m));  // the dip to 2.50 comes after the move paid
        var r = MomentumScenarioEvaluator.Evaluate(Break(2.70m, [3.00m], confirm: [2.50m], requiresConfirm: true), bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.ConfirmHit, Is.False);
            Assert.That(r.PlanPnlPct, Is.Null);
            Assert.That(r.Verdict, Is.EqualTo(MomentumVerdicts.TargetHit));
        });
    }

    [Test]
    public void AMorningUpdate_CountsTriggersFromItsPublishMinute()
    {
        var bars = Bars(
            ("07:00", 2.60m, 2.80m, 2.58m, 2.62m),   // over 2.70 before the video went out
            ("08:00", 2.62m, 2.66m, 2.60m, 2.64m),
            ("08:30", 2.64m, 2.75m, 2.63m, 2.74m));  // the break that counts
        var r = MomentumScenarioEvaluator.Evaluate(Break(2.70m, [3.00m]), bars, notBeforeUtc: bars[1].StartUtc);
        Assert.That(r.TriggerUtc, Is.EqualTo(bars[2].StartUtc));
    }

    [Test]
    public void OpeningOverTheTrigger_IsAGapThroughEnteredAtTheOpen()
    {
        var bars = Bars(("04:00", 3.00m, 3.10m, 2.95m, 3.05m), ("04:01", 3.05m, 3.60m, 3.00m, 3.55m));
        var r = MomentumScenarioEvaluator.Evaluate(Break(2.70m, [3.50m]), bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.TriggerGapThrough, Is.True);
            Assert.That(r.TriggerPrice, Is.EqualTo(3.00m));
            Assert.That(r.ExitPrice, Is.EqualTo(3.50m));
        });
    }

    [Test]
    public void Reclaim_NeedsATradeUnderTheLevelThenACloseBackOver()
    {
        var bars = Bars(
            ("09:30", 2.70m, 2.75m, 2.60m, 2.65m),   // above 2.50 all bar: no reclaim yet
            ("09:31", 2.65m, 2.66m, 2.40m, 2.45m),   // flush under 2.50
            ("09:32", 2.45m, 2.58m, 2.44m, 2.56m),   // close back over: trigger, entry at the close
            ("09:33", 2.56m, 2.80m, 2.55m, 2.75m));
        var spec = new ScenarioSpec("reclaim", new ScenarioLevel(2.50m), [], [2.75m], null, false);
        var r = MomentumScenarioEvaluator.Evaluate(spec, bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.TriggerUtc, Is.EqualTo(bars[2].StartUtc));
            Assert.That(r.TriggerPrice, Is.EqualTo(2.56m));
            Assert.That(r.ExitReason, Is.EqualTo("target"));
            Assert.That(r.ExitPrice, Is.EqualTo(2.75m));
        });
    }

    [Test]
    public void Hold_TriggersOnACloseOverThePullbackBarsHigh()
    {
        var bars = Bars(
            ("09:30", 2.80m, 2.85m, 2.70m, 2.72m),
            ("09:31", 2.60m, 2.62m, 2.55m, 2.58m),   // pulls into 2.50 x 1.03: the pullback bar (high 2.62)
            ("09:32", 2.58m, 2.61m, 2.56m, 2.60m),   // closes under 2.62: not yet
            ("09:33", 2.60m, 2.70m, 2.59m, 2.68m));  // close 2.68 > 2.62: higher low confirmed
        var spec = new ScenarioSpec("hold", new ScenarioLevel(2.50m), [], [3.30m], null, false);
        var r = MomentumScenarioEvaluator.Evaluate(spec, bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.TriggerUtc, Is.EqualTo(bars[3].StartUtc));
            Assert.That(r.TriggerPrice, Is.EqualTo(2.68m));
            Assert.That(r.ExitReason, Is.EqualTo("time"));
        });
    }

    [Test]
    public void VwapConfirm_UsesTheSessionVwapAndBecomesTheDefaultStop()
    {
        var bars = Bars(
            ("09:30", 2.00m, 2.00m, 2.00m, 2.00m),
            ("09:31", 2.00m, 2.80m, 2.00m, 2.75m));
        var spec = new ScenarioSpec("break", new ScenarioLevel(2.50m), [new ScenarioLevel(null, Vwap: true)], [3.00m], null, true);
        var r = MomentumScenarioEvaluator.Evaluate(spec, bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.InvalidationSource, Is.EqualTo("default-vwap"));
            Assert.That(r.InvalidationLevel, Is.Null);
            Assert.That(bars[1].Vwap, Is.GreaterThan(2.00m).And.LessThan(2.75m));
        });
    }

    [Test]
    public void NoTargetHit_ExitsAt1555()
    {
        var bars = Bars(
            ("09:30", 2.60m, 2.75m, 2.58m, 2.72m),
            ("12:00", 2.72m, 2.85m, 2.70m, 2.80m),
            ("15:55", 2.80m, 2.82m, 2.78m, 2.81m),
            ("15:59", 2.81m, 2.90m, 2.80m, 2.88m));
        var r = MomentumScenarioEvaluator.Evaluate(Break(2.70m, [3.50m]), bars);
        Assert.Multiple(() =>
        {
            Assert.That(r.ExitReason, Is.EqualTo("time"));
            Assert.That(r.ExitPrice, Is.EqualTo(2.81m));
            Assert.That(r.Verdict, Is.EqualTo(MomentumVerdicts.TriggeredNoTarget));
            Assert.That(r.CloseVsTriggerPct, Is.EqualTo(6.67).Within(0.01));
        });
    }
}
