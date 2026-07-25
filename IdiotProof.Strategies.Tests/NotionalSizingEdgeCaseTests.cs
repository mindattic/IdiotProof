using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Tests for notional-amount sizing through the <see cref="DslStrategy"/>
/// evaluation path.  The TradeSignal that fires should reflect whether a
/// strategy was authored with QuantityNotional($N) or Quantity(N shares), and
/// the signal's sizing hint must be correct for downstream order placement.
///
/// "Real money" invariant: a strategy configured for $2500 notional must
/// never silently place a 1-share order, and a strategy configured for 100
/// shares must never silently use notional sizing.
/// </summary>
public class NotionalSizingEdgeCaseTests
{
    private static IReadOnlyList<Candle> Bars(double price = 10.0)
    {
        var start = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc);
        return
        [
            new Candle
            {
                Symbol = "TEST",
                StartUtc = start, EndUtc = start.AddMinutes(1),
                Open = (decimal)price, High = (decimal)(price * 1.02),
                Low = (decimal)(price * 0.99), Close = (decimal)price,
                Volume = 2_000_000,
            },
        ];
    }

    // ── Notional sizing ───────────────────────────────────────────────────

    [Test]
    public void QuantityNotional_Amount_SetsNotionalOnDefinition()
    {
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5)
            .QuantityNotional(2500).Build();
        Assert.That(def.NotionalAmount, Is.EqualTo(2500m),
            "QuantityNotional() must populate NotionalAmount on the definition");
    }

    [Test]
    public void QuantityNotional_Zero_IsStoredAsZero()
    {
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5)
            .QuantityNotional(0).Build();
        Assert.That(def.NotionalAmount, Is.EqualTo(0m));
    }

    [Test]
    public void QuantityNotional_FractionalDollar_RoundTrips()
    {
        // Notional of $1250.50 must survive the JSON round-trip intact
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5)
            .QuantityNotional(1250.50m).Build();
        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));
        Assert.That(restored.NotionalAmount, Is.EqualTo(1250.50m));
    }

    [Test]
    public void Quantity_SharesMode_LeavesNotionalNull()
    {
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Quantity(100).Build();
        Assert.That(def.NotionalAmount, Is.Null,
            "a shares-based strategy must have null NotionalAmount");
    }

    [Test]
    public void Quantity_SharesMode_SetsQuantity()
    {
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Quantity(100).Build();
        Assert.That(def.Quantity, Is.EqualTo(100));
    }

    // ── JSON round-trip for both sizing modes ─────────────────────────────

    [Test]
    public void QuantityNotional_RoundTripsViaJson()
    {
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5)
            .QuantityNotional(5000).Build();
        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));
        // Only assert NotionalAmount survived — Quantity is an int (not int?) so
        // it always has a value and is irrelevant for a notional-sized strategy.
        Assert.That(restored.NotionalAmount, Is.EqualTo(5000m), "NotionalAmount");
    }

    [Test]
    public void Shares_RoundTripsViaJson()
    {
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Quantity(50).Build();
        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));
        Assert.Multiple(() =>
        {
            Assert.That(restored.Quantity,       Is.EqualTo(50),  "Quantity");
            Assert.That(restored.NotionalAmount, Is.Null,         "NotionalAmount must be null for shares strategy");
        });
    }

    // ── Notional survives branch resolution ───────────────────────────────

    [Test]
    public void QuantityNotional_SurvivesResolveBranches_WhenNoBranchMatches()
    {
        // NotionalAmount was one of the fields that was always copied (pre-fix).
        // This regression guard ensures it doesn't get lost in future refactors.
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5)
            .QuantityNotional(3000).Build();

        var block = new ConditionalBlock();
        block.Branches.Add(new ConditionalBranch
        {
            Condition = new PriceLevelCondition(PriceLevelType.HoldsAbove, 999_999),
            Overrides = new StrategyOverrides { Direction = TradeDirection.Short },
        });
        def.ConditionalBlocks.Add(block);

        // Evaluate through DslStrategy (exercises the clone path)
        new DslStrategy(def).Evaluate("TEST", Bars(), new StrategyContext());

        // The base definition's NotionalAmount must not be clobbered
        Assert.That(def.NotionalAmount, Is.EqualTo(3000m),
            "NotionalAmount must not be clobbered by branch resolution");
    }

    // ── Gapper profile: defaultNotional flows through to definition ───────

    [Test]
    public void GapperProfile_DefaultNotional_AppearsOnDefinition()
    {
        var profile = new GapperProfile
        {
            MinGapPercent = 5, MaxGapPercent = 20, MinVolumeRatio = 2,
            MinPrice = 1, MaxPrice = 50,
            EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
            StopLossPercent = 5, TrailingStopPercent = 8, PeakGivebackPercent = 25,
            ArmExitAtEt = "09:15", SellByEt = "09:28",
            DefaultNotional = 1500m,
        };
        var def = GapperScriptFactory.Compose("NVDA", profile).Build();
        Assert.That(def.NotionalAmount, Is.EqualTo(1500m),
            "GapperScriptFactory must wire DefaultNotional → StrategyDefinition.NotionalAmount");
    }

    [Test]
    public void GapperProfile_DefaultNotional_RoundTripsViaJson()
    {
        var profile = new GapperProfile
        {
            MinGapPercent = 5, MaxGapPercent = 20, MinVolumeRatio = 2,
            MinPrice = 1, MaxPrice = 50,
            EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
            StopLossPercent = 5, TrailingStopPercent = 8, PeakGivebackPercent = 25,
            ArmExitAtEt = "09:15", SellByEt = "09:28",
            DefaultNotional = 2000m,
        };
        var def = GapperScriptFactory.Compose("AAPL", profile).Build();
        var restored = StrategyJson.Deserialize(StrategyJson.Serialize(def));
        Assert.That(restored.NotionalAmount, Is.EqualTo(2000m));
    }

    // ── Signal still fires regardless of sizing mode ──────────────────────

    [Test]
    public void NotionalStrategy_StillFiresSignal()
    {
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).QuantityNotional(2500).Build();
        var signals = new DslStrategy(def).Evaluate("TEST", Bars(), new StrategyContext());
        Assert.That(signals, Has.Count.EqualTo(1));
    }

    [Test]
    public void SharesStrategy_StillFiresSignal()
    {
        var def = Stock.Ticker("TEST").Long().StopLossPercent(5).Quantity(100).Build();
        var signals = new DslStrategy(def).Evaluate("TEST", Bars(), new StrategyContext());
        Assert.That(signals, Has.Count.EqualTo(1));
    }
}
