using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Exhaustive permutation tests for the six strategy phases.
///
/// Phase model:
///   1 Setup   — ticker, session, account, window
///   2 Filters — always-on regime preconditions (EmaStack, AdxAbove)
///   3 Entry   — trigger conditions ("the fire")
///   4 Order   — direction, quantity
///   5 Risk    — stop loss, trailing
///   6 Exit    — targets, time exits, condition exits
///
/// Key architectural fact: StrategyDefinition has a single flat
/// EntryConditions list; filter-phase conditions live there too,
/// tagged with Phase = StrategyPhase.Filters.  DslStrategy evaluates
/// all conditions in insertion order with AND semantics — phase-blind.
///
/// Why these tests matter: a filter-phase condition that doesn't evaluate
/// correctly (false negative) fires the strategy when regime conditions
/// are wrong; a false positive blocks valid entries.  Either is a money
/// event on the live feed.
///
/// Coverage
/// ────────
///   Filter-gate via pipeline ........... 10 tests  (EmaStack / AdxAbove block/pass)
///   Phase metadata correctness ......... 9  tests  (Phase property, JSON round-trip)
///   Order-phase quantity round-trip .... 6  tests  (Shares, Notional, both directions)
///   Risk-phase combinations ............. 8  tests  (StopLoss%, StopLossPrice, Trailing)
///   Exit × Direction full round-trip ... 14 tests  (7 exit types × 2 directions)
///   Complete-strategy pipeline ......... 6  tests  (all 6 phases wired up)
///   Phase tag not used by DslStrategy .. 2  tests  (evaluation is phase-blind)
/// </summary>
public class StrategyPhasePermutationTests
{
    // ── Candle / context factories ────────────────────────────────────────

    private static readonly DateTime SnapUtc = new(2026, 7, 17, 9, 15, 0, DateTimeKind.Utc);

    /// 1 bar — VWAP computable; EMA9/EMA21/ADX/RSI/MACD all null (insufficient).
    private static IReadOnlyList<Candle> OneBars(double price = 10.0) =>
    [
        new Candle
        {
            Symbol   = "TEST",
            StartUtc = SnapUtc.AddMinutes(-1),
            EndUtc   = SnapUtc,
            Open     = (decimal)(price * 0.99),
            High     = (decimal)(price * 1.02),
            Low      = (decimal)(price * 0.98),
            Close    = (decimal)price,
            Volume   = 2_000_000,
        },
    ];

    /// 30 bars with monotonically rising close — enough for EMA9/21/ADX/RSI/MACD.
    private static IReadOnlyList<Candle> ThirtyBars(double startPrice = 9.0, double endPrice = 11.0)
        => Enumerable.Range(0, 30).Select(i =>
        {
            double p = startPrice + (endPrice - startPrice) * i / 29;
            return new Candle
            {
                Symbol   = "TEST",
                StartUtc = SnapUtc.AddMinutes(-30 + i),
                EndUtc   = SnapUtc.AddMinutes(-29 + i),
                Open     = (decimal)(p * 0.99),
                High     = (decimal)(p * 1.02),
                Low      = (decimal)(p * 0.98),
                Close    = (decimal)p,
                Volume   = 2_000_000,
            };
        }).ToList();

    private static StrategyContext Context(double previousClose = 9.0) => new()
    {
        PreviousClose     = (decimal)previousClose,
        EvaluationTimeUtc = SnapUtc,
    };

    private static IReadOnlyList<TradeSignal> RunPipeline(
        StrategyDefinition def, IReadOnlyList<Candle>? bars = null, double previousClose = 9.0)
    {
        bars ??= OneBars();
        return new DslStrategy(def).Evaluate("TEST", bars, Context(previousClose));
    }

    private static StrategyDefinition DefWith(TradeDirection dir, params ICondition[] conditions)
    {
        var def = new StrategyDefinition
        {
            Symbol          = "TEST",
            Direction       = dir,
            StopLossPercent = 5,
            Quantity        = 1,
        };
        foreach (var c in conditions)
            def.EntryConditions.Add(c);
        return def;
    }

    // ── Phase 2: Filter-gate via pipeline ────────────────────────────────

    [Test]
    public void FilterGate_EmaStack_BlocksEntry_With1Bar()
    {
        // With 1 bar, EMA9/EMA21 are null — EmaStack evaluates to false.
        var filter = new IndicatorCondition(IndicatorType.EmaStack, 9, 21, StrategyPhase.Filters);
        var entry  = new PriceBandCondition(0, 1000); // always passes
        var signals = RunPipeline(DefWith(TradeDirection.Long, filter, entry));
        Assert.That(signals.Count, Is.EqualTo(0),
            "EmaStack filter must block entry when EMA values are null (insufficient bars)");
    }

    [Test]
    public void FilterGate_AdxAbove_BlocksEntry_With1Bar()
    {
        // With 1 bar, ADX is null — AdxAbove evaluates to false.
        var filter = new IndicatorCondition(IndicatorType.AdxAbove, 20, phase: StrategyPhase.Filters);
        var entry  = new PriceBandCondition(0, 1000);
        var signals = RunPipeline(DefWith(TradeDirection.Long, filter, entry));
        Assert.That(signals.Count, Is.EqualTo(0),
            "AdxAbove filter must block entry when ADX is null (insufficient bars)");
    }

    [Test]
    public void FilterGate_EntryOnly_PriceBand_Fires_With1Bar()
    {
        // Baseline: no filter — PriceBandCondition(0,1000) always passes.
        var entry   = new PriceBandCondition(0, 1000);
        var signals = RunPipeline(DefWith(TradeDirection.Long, entry));
        Assert.That(signals.Count, Is.EqualTo(1),
            "No filter + always-pass entry must fire (confirms the filter gate is what blocks)");
    }

    [Test]
    public void FilterGate_EmaStack_Short_BlocksEntry_With1Bar()
    {
        var filter = new IndicatorCondition(IndicatorType.EmaStack, 9, 21, StrategyPhase.Filters);
        var entry  = new PriceBandCondition(0, 1000);
        var def    = DefWith(TradeDirection.Short, filter, entry);
        Assert.That(RunPipeline(def).Count, Is.EqualTo(0),
            "EmaStack filter blocks Short entry when EMA null");
    }

    [Test]
    public void FilterGate_AdxAbove_Short_BlocksEntry_With1Bar()
    {
        var filter = new IndicatorCondition(IndicatorType.AdxAbove, 20, phase: StrategyPhase.Filters);
        var entry  = new PriceBandCondition(0, 1000);
        var def    = DefWith(TradeDirection.Short, filter, entry);
        Assert.That(RunPipeline(def).Count, Is.EqualTo(0),
            "AdxAbove filter blocks Short entry when ADX null");
    }

    [Test]
    public void FilterGate_BothFilters_BlocksEntry_With1Bar()
    {
        var emaFilter = new IndicatorCondition(IndicatorType.EmaStack, 9, 21, StrategyPhase.Filters);
        var adxFilter = new IndicatorCondition(IndicatorType.AdxAbove, 20, phase: StrategyPhase.Filters);
        var entry     = new PriceBandCondition(0, 1000);
        var signals   = RunPipeline(DefWith(TradeDirection.Long, emaFilter, adxFilter, entry));
        Assert.That(signals.Count, Is.EqualTo(0),
            "Two failing filters must both block entry (AND semantics)");
    }

    [Test]
    public void FilterGate_ViaBuilder_RequireEmaStack_BlocksEntry_With1Bar()
    {
        // Using the actual builder path (RequireEmaStack method), not a manually constructed condition.
        var def = Stock.Ticker("TEST").Long()
            .RequireEmaStack(9, 21)
            .StopLossPercent(5)
            .Build();
        // Add a deterministic entry condition so the filter is the only variable.
        def.EntryConditions.Add(new PriceBandCondition(0, 1000));
        Assert.That(RunPipeline(def).Count, Is.EqualTo(0),
            "RequireEmaStack (builder path) must block entry when EMA not computed");
    }

    [Test]
    public void FilterGate_ViaBuilder_RequireAdxAbove_BlocksEntry_With1Bar()
    {
        var def = Stock.Ticker("TEST").Long()
            .RequireAdxAbove(20)
            .StopLossPercent(5)
            .Build();
        def.EntryConditions.Add(new PriceBandCondition(0, 1000));
        Assert.That(RunPipeline(def).Count, Is.EqualTo(0),
            "RequireAdxAbove (builder path) must block entry when ADX not computed");
    }

    [Test]
    public void FilterGate_EmaStack_Passes_With30Bars_RisingPrice()
    {
        // With 30 rising-price bars, EMA9 > EMA21 (fast above slow = stack OK).
        var filter = new IndicatorCondition(IndicatorType.EmaStack, 9, 21, StrategyPhase.Filters);
        var entry  = new PriceBandCondition(0, 1000);
        var bars   = ThirtyBars(startPrice: 9.0, endPrice: 11.0);
        var signals = RunPipeline(DefWith(TradeDirection.Long, filter, entry), bars);
        Assert.That(signals.Count, Is.EqualTo(1),
            "EmaStack filter must PASS with 30 rising-price bars (EMA9 > EMA21)");
    }

    [Test]
    public void FilterGate_AdxAbove_Passes_With30Bars()
    {
        // 30 bars gives enough history to compute ADX; trending price → ADX > threshold.
        // We can't guarantee the exact ADX value, so we use a very low threshold (1).
        var filter = new IndicatorCondition(IndicatorType.AdxAbove, 1, phase: StrategyPhase.Filters);
        var entry  = new PriceBandCondition(0, 1000);
        var bars   = ThirtyBars(startPrice: 9.0, endPrice: 11.0);
        var signals = RunPipeline(DefWith(TradeDirection.Long, filter, entry), bars);
        Assert.That(signals.Count, Is.EqualTo(1),
            "AdxAbove(1) filter must PASS with 30 trending-price bars (ADX > 1)");
    }

    // ── Phase 2: Phase metadata correctness ──────────────────────────────

    [Test]
    public void FilterCondition_EmaStack_HasFiltersPhase_BeforeRoundTrip()
    {
        var def = Stock.Ticker("TEST").Long().RequireEmaStack(9, 21).StopLossPercent(5).Build();
        Assert.That(def.EntryConditions, Has.Count.EqualTo(1));
        Assert.That(def.EntryConditions[0].Phase, Is.EqualTo(StrategyPhase.Filters),
            "RequireEmaStack must add a Filters-phase condition to EntryConditions");
    }

    [Test]
    public void FilterCondition_AdxAbove_HasFiltersPhase_BeforeRoundTrip()
    {
        var def = Stock.Ticker("TEST").Long().RequireAdxAbove(25).StopLossPercent(5).Build();
        Assert.That(def.EntryConditions, Has.Count.EqualTo(1));
        Assert.That(def.EntryConditions[0].Phase, Is.EqualTo(StrategyPhase.Filters),
            "RequireAdxAbove must add a Filters-phase condition to EntryConditions");
    }

    [Test]
    public void FilterCondition_Phase_PreservedAfterJsonRoundTrip_EmaStack()
    {
        var def      = Stock.Ticker("TEST").Long().RequireEmaStack(9, 21).StopLossPercent(5).Build();
        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        Assert.That(restored.EntryConditions, Has.Count.EqualTo(1));
        Assert.That(restored.EntryConditions[0].Phase, Is.EqualTo(StrategyPhase.Filters),
            "EmaStack filter condition Phase must survive JSON round-trip");
    }

    [Test]
    public void FilterCondition_Phase_PreservedAfterJsonRoundTrip_AdxAbove()
    {
        var def      = Stock.Ticker("TEST").Long().RequireAdxAbove(20).StopLossPercent(5).Build();
        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        Assert.That(restored.EntryConditions, Has.Count.EqualTo(1));
        Assert.That(restored.EntryConditions[0].Phase, Is.EqualTo(StrategyPhase.Filters),
            "AdxAbove filter condition Phase must survive JSON round-trip");
    }

    [Test]
    public void EntryCondition_HasEntryPhase_ByDefault()
    {
        var cond = new IndicatorCondition(IndicatorType.VwapAbove);
        Assert.That(cond.Phase, Is.EqualTo(StrategyPhase.Entry),
            "IndicatorCondition defaults to Entry phase");
    }

    [Test]
    public void FilterAndEntryCondition_Phase_BothPreservedAfterRoundTrip()
    {
        var def = Stock.Ticker("TEST").Long()
            .RequireEmaStack(9, 21)
            .RequireAdxAbove(20)
            .StopLossPercent(5)
            .Build();
        // Add a manual entry-phase condition
        def.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapAbove));

        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        Assert.Multiple(() =>
        {
            Assert.That(restored.EntryConditions, Has.Count.EqualTo(3));
            Assert.That(restored.EntryConditions[0].Phase, Is.EqualTo(StrategyPhase.Filters),
                "First condition (EmaStack) must be Filters");
            Assert.That(restored.EntryConditions[1].Phase, Is.EqualTo(StrategyPhase.Filters),
                "Second condition (AdxAbove) must be Filters");
            Assert.That(restored.EntryConditions[2].Phase, Is.EqualTo(StrategyPhase.Entry),
                "Third condition (IsAboveVwap) must be Entry");
        });
    }

    [Test]
    public void ManualFiltersPhaseCondition_PreservesPhase_AfterRoundTrip()
    {
        var cond = new IndicatorCondition(IndicatorType.VwapAbove, phase: StrategyPhase.Filters);
        var def  = DefWith(TradeDirection.Long, cond);
        var json = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        Assert.That(restored.EntryConditions[0].Phase, Is.EqualTo(StrategyPhase.Filters),
            "Manually set Filters phase must survive round-trip");
    }

    [Test]
    public void DslStrategy_IsPhaseBlind_SameResultRegardlessOfPhaseTag()
    {
        // DslStrategy evaluates all EntryConditions identically regardless of Phase tag.
        // Changing Phase on a condition must not change evaluation outcome.
        var condEntry   = new IndicatorCondition(IndicatorType.VwapAbove, phase: StrategyPhase.Entry);
        var condFilters = new IndicatorCondition(IndicatorType.VwapAbove, phase: StrategyPhase.Filters);
        var signalsEntry   = RunPipeline(DefWith(TradeDirection.Long, condEntry));
        var signalsFilters = RunPipeline(DefWith(TradeDirection.Long, condFilters));
        Assert.That(signalsFilters.Count, Is.EqualTo(signalsEntry.Count),
            "DslStrategy must produce identical results regardless of condition Phase tag");
    }

    // ── Phase 4: Order — Quantity round-trip ─────────────────────────────

    private static readonly (int Qty, TradeDirection Dir, string Label)[] QuantityCases =
    [
        (1, TradeDirection.Long,  "Long 1 share"),
        (5, TradeDirection.Long,  "Long 5 shares"),
        (1, TradeDirection.Short, "Short 1 share"),
        (5, TradeDirection.Short, "Short 5 shares"),
    ];

    [TestCaseSource(nameof(QuantityCases))]
    public void Quantity_Shares_RoundTrips_Correctly((int qty, TradeDirection dir, string label) tc)
    {
        var def      = new StrategyDefinition { Symbol = "TEST", Direction = tc.dir, Quantity = tc.qty, StopLossPercent = 5 };
        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        Assert.That(restored.Quantity, Is.EqualTo(tc.qty), $"{tc.label}: Quantity must round-trip");
    }

    [Test]
    public void Quantity_Notional_RoundTrips_Correctly()
    {
        var def = new StrategyDefinition
        {
            Symbol          = "TEST",
            Direction       = TradeDirection.Long,
            NotionalAmount  = 5000,
            StopLossPercent = 5,
        };
        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        Assert.That(restored.NotionalAmount, Is.EqualTo(5000),
            "NotionalAmount must round-trip correctly");
    }

    [Test]
    public void Quantity_NotionalShort_RoundTrips_Correctly()
    {
        var def = new StrategyDefinition
        {
            Symbol          = "TEST",
            Direction       = TradeDirection.Short,
            NotionalAmount  = 2500,
            StopLossPercent = 5,
        };
        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        Assert.That(restored.NotionalAmount, Is.EqualTo(2500));
    }

    // ── Phase 5: Risk — stop-loss combinations ───────────────────────────

    private static readonly object?[] RiskCases =
    [
        new object?[] { (double?)3.0,  (double?)null, (double?)null, "StopLossPercent=3" },
        new object?[] { (double?)5.0,  (double?)null, (double?)null, "StopLossPercent=5" },
        new object?[] { (double?)null, (double?)9.5,  (double?)null, "StopLossPrice=9.5" },
        new object?[] { (double?)null, (double?)null, (double?)8.0,  "TrailingStopPercent=8" },
        new object?[] { (double?)5.0,  (double?)null, (double?)8.0,  "StopLossPercent+TrailingStop" },
        new object?[] { (double?)null, (double?)9.5,  (double?)8.0,  "StopLossPrice+TrailingStop" },
        new object?[] { (double?)3.0,  (double?)null, (double?)null, "StopLossPercent=3 Short" },
        new object?[] { (double?)null, (double?)9.5,  (double?)8.0,  "StopLossPrice+TrailingStop Short" },
    ];

    [TestCaseSource(nameof(RiskCases))]
    public void Risk_Combination_RoundTrips(
        double? stopPct, double? stopPrice, double? trailingPct, string label)
    {
        var dir = label.EndsWith("Short") ? TradeDirection.Short : TradeDirection.Long;
        var def = new StrategyDefinition
        {
            Symbol              = "TEST",
            Direction           = dir,
            Quantity            = 1,
            StopLossPercent     = stopPct,
            StopLossPrice       = stopPrice,
            TrailingStopPercent = trailingPct,
        };
        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        Assert.Multiple(() =>
        {
            Assert.That(restored.StopLossPercent,     Is.EqualTo(def.StopLossPercent),     $"{label}: StopLossPercent");
            Assert.That(restored.StopLossPrice,       Is.EqualTo(def.StopLossPrice),       $"{label}: StopLossPrice");
            Assert.That(restored.TrailingStopPercent, Is.EqualTo(def.TrailingStopPercent), $"{label}: TrailingStopPercent");
        });
    }

    // ── Phase 6: Exit × Direction — full round-trip ───────────────────────

    private static readonly (string Label, Func<StrategyBuilder, StrategyBuilder> Exit)[] ExitTypeCases =
    [
        ("TakeProfit(12)",         b => b.TakeProfit(12.0)),
        ("TakeProfit(11,12,14)",   b => b.TakeProfit(11.0, 12.0, 14.0)),
        ("SellBy(09:29)",          b => b.SellBy("09:29")),
        ("TrailingStop(8)",        b => b.TrailingStopLoss(8)),
        ("PeakGiveback(25)",       b => b.PeakGiveback(25, "09:15")),
        ("PeakGiveback(30,09:00)", b => b.PeakGiveback(30, "09:00")),
        ("MultiExit",              b => b.TakeProfit(12.0).SellBy("09:29")),
    ];

    private static IEnumerable<TestCaseData> ExitDirectionCases()
    {
        foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
            foreach (var (label, exitFn) in ExitTypeCases)
                yield return new TestCaseData(dir, exitFn, label)
                    .SetName($"ExitPhase_{label}_{dir}");
    }

    [TestCaseSource(nameof(ExitDirectionCases))]
    public void ExitPhase_RoundTrips_Correctly(
        TradeDirection dir, Func<StrategyBuilder, StrategyBuilder> exitFn, string label)
    {
        var builder = dir == TradeDirection.Long
            ? Stock.Ticker("TEST").Long().StopLossPercent(5)
            : Stock.Ticker("TEST").Short().StopLossPercent(5);
        var def      = exitFn(builder).Build();
        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        var json2    = StrategyJson.Serialize(restored);
        Assert.That(json2, Is.EqualTo(json),
            $"{dir}/{label}: JSON must be identical after double round-trip");
    }

    // ── Complete strategy (all 6 phases) ────────────────────────────────

    [Test]
    public void CompleteStrategy_AllPhases_Long_RoundTrips()
    {
        // Setup: TEST/Long
        // Filters: EmaStack
        // Entry: IsAboveVwap (added manually after build since builder appends in order)
        // Order: 1 share, Long
        // Risk: StopLossPercent=5 + TrailingStop=8
        // Exit: TakeProfit(12)
        var def = Stock.Ticker("TEST").Long()
            .RequireEmaStack(9, 21)
            .RequireAdxAbove(20)
            .StopLossPercent(5)
            .TrailingStopLoss(8)
            .TakeProfit(12.0)
            .SellBy("09:29")
            .Build();
        def.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapAbove));

        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        var json2    = StrategyJson.Serialize(restored);
        Assert.Multiple(() =>
        {
            Assert.That(json2, Is.EqualTo(json), "Full six-phase Long strategy must double-round-trip identically");
            Assert.That(restored.Direction, Is.EqualTo(TradeDirection.Long));
            Assert.That(restored.StopLossPercent, Is.EqualTo(5));
            Assert.That(restored.TrailingStopPercent, Is.EqualTo(8));
            Assert.That(restored.TakeProfitPrice, Is.EqualTo(12.0));
            Assert.That(restored.EntryConditions, Has.Count.EqualTo(3));
            Assert.That(restored.EntryConditions[0].Phase, Is.EqualTo(StrategyPhase.Filters));
            Assert.That(restored.EntryConditions[1].Phase, Is.EqualTo(StrategyPhase.Filters));
            Assert.That(restored.EntryConditions[2].Phase, Is.EqualTo(StrategyPhase.Entry));
        });
    }

    [Test]
    public void CompleteStrategy_AllPhases_Short_RoundTrips()
    {
        var def = Stock.Ticker("TEST").Short()
            .RequireEmaStack(9, 21)
            .StopLossPercent(5)
            .TrailingStopLoss(8)
            .TakeProfit(8.0)
            .SellBy("09:29")
            .Build();
        def.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapBelow));

        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        var json2    = StrategyJson.Serialize(restored);
        Assert.Multiple(() =>
        {
            Assert.That(json2, Is.EqualTo(json), "Full six-phase Short strategy must double-round-trip identically");
            Assert.That(restored.Direction, Is.EqualTo(TradeDirection.Short));
        });
    }

    [Test]
    public void CompleteStrategy_WithPeakGiveback_AllPhases_RoundTrips()
    {
        var def = Stock.Ticker("NVDA").Long()
            .RequireEmaStack(9, 21)
            .RequireAdxAbove(25)
            .StopLossPercent(5)
            .PeakGiveback(25, "09:15")
            .Build();
        def.EntryConditions.Add(new IndicatorCondition(IndicatorType.VwapAbove));

        var json     = StrategyJson.Serialize(def);
        var restored = StrategyJson.Deserialize(json);
        var json2    = StrategyJson.Serialize(restored);
        Assert.Multiple(() =>
        {
            Assert.That(json2, Is.EqualTo(json));
            Assert.That(restored.PeakGivebackPercent, Is.EqualTo(25));
            Assert.That(restored.EntryConditions[0].Phase, Is.EqualTo(StrategyPhase.Filters));
            Assert.That(restored.EntryConditions[1].Phase, Is.EqualTo(StrategyPhase.Filters));
        });
    }

    [Test]
    public void CompleteStrategy_WithFilters_PipelineBlock_With1Bar()
    {
        // A complete strategy with filters must NOT fire with 1 bar (filters block).
        var def = Stock.Ticker("TEST").Long()
            .RequireEmaStack(9, 21)
            .StopLossPercent(5)
            .TakeProfit(12.0)
            .Build();
        def.EntryConditions.Add(new PriceBandCondition(0, 1000));
        var signals = RunPipeline(def);
        Assert.That(signals.Count, Is.EqualTo(0),
            "Filter-gated complete strategy must not fire when EMA is unavailable (1 bar)");
    }

    [Test]
    public void CompleteStrategy_WithFilters_PipelinePass_With30Bars()
    {
        // Same strategy with 30 bars: EmaStack computes and passes (rising price).
        var def = Stock.Ticker("TEST").Long()
            .RequireEmaStack(9, 21)
            .StopLossPercent(5)
            .TakeProfit(12.0)
            .Build();
        def.EntryConditions.Add(new PriceBandCondition(0, 1000));
        var bars    = ThirtyBars(startPrice: 9.0, endPrice: 11.0);
        var signals = RunPipeline(def, bars);
        Assert.That(signals.Count, Is.EqualTo(1),
            "Filter-gated complete strategy must fire when EMA is computed and stack condition passes");
    }

    [Test]
    public void CompleteStrategy_NoFilters_PipelinePass_With1Bar()
    {
        var def = Stock.Ticker("TEST").Long()
            .StopLossPercent(5)
            .TakeProfit(12.0)
            .Build();
        def.EntryConditions.Add(new PriceBandCondition(0, 1000));
        var signals = RunPipeline(def);
        Assert.That(signals.Count, Is.EqualTo(1),
            "No-filter strategy with always-pass entry must fire with 1 bar");
    }
}
