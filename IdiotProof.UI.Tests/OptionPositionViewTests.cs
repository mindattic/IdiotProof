using IdiotProof.Models;
using IdiotProof.Shared.Options;
using IdiotProof.UI.Components.Options;

namespace IdiotProof.UI.Tests;

/// <summary>P&L as the position tracker shows it: contracts × 100, signed for long vs short, and the sell-nudge warm-up state.</summary>
[TestFixture]
public class OptionPositionViewTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 5, 15, 0, 0, DateTimeKind.Utc);

    private static OptionContract Contract() => new()
    {
        OccSymbol = OptionContract.BuildOcc("BE", new DateOnly(2026, 12, 18), OptionRight.Call, 40m),
        UnderlyingSymbol = "BE", Expiration = new DateOnly(2026, 12, 18), Strike = 40m, Right = OptionRight.Call,
    };

    private static Position Pos(decimal qty, decimal avg) => new()
    {
        Symbol = Contract().OccSymbol, Quantity = qty, AveragePrice = avg, AssetClass = AssetClass.Option, Option = Contract(),
    };

    private static OptionQuote Mid(decimal mid) => new("X", mid - 0.05m, mid + 0.05m, null, null, null, NowUtc);

    private static OptionPositionView View(Position p, OptionQuote? q, SellSignal? signal = null, int observations = 0) =>
        new(p, q, 45m, null, null, "—", signal, observations);

    [Test]
    public void Long_ProfitsWhenMidRises()
    {
        var v = View(Pos(2, 9.50m), Mid(12.00m));
        Assert.Multiple(() =>
        {
            Assert.That(v.CostBasis, Is.EqualTo(1900m), "2 × $9.50 × 100");
            Assert.That(v.MarketValueNow, Is.EqualTo(2400m));
            Assert.That(v.UnrealizedPnl, Is.EqualTo(500m));
            Assert.That(v.UnrealizedPnlPercent, Is.EqualTo(26.3m));
        });
    }

    [Test]
    public void Short_LosesWhenMidRises()
    {
        var v = View(Pos(-2, 9.50m), Mid(12.00m));
        Assert.Multiple(() =>
        {
            Assert.That(v.CostBasis, Is.EqualTo(1900m), "basis uses |qty|");
            Assert.That(v.UnrealizedPnl, Is.EqualTo(-500m));
            Assert.That(v.UnrealizedPnlPercent, Is.EqualTo(-26.3m));
        });
    }

    [Test]
    public void NoQuote_NoPnl()
    {
        var v = View(Pos(1, 9.50m), null);
        Assert.Multiple(() =>
        {
            Assert.That(v.Mid, Is.Null);
            Assert.That(v.MarketValueNow, Is.Null);
            Assert.That(v.UnrealizedPnl, Is.Null);
            Assert.That(v.UnrealizedPnlPercent, Is.Null);
        });
    }

    [Test]
    public void ZeroBidAsk_TreatedAsNoMid()
    {
        var v = View(Pos(1, 9.50m), new OptionQuote("X", 0m, 0m, null, null, null, NowUtc));
        Assert.That(v.Mid, Is.Null);
    }

    [Test]
    public void WarmingUp_OnlyForLongs_UntilMinObservations()
    {
        Assert.Multiple(() =>
        {
            Assert.That(View(Pos(1, 9.5m), Mid(10m), observations: 0).IsWarmingUp, Is.True);
            Assert.That(View(Pos(1, 9.5m), Mid(10m), observations: SellSignalEvaluator.MinObservations - 1).IsWarmingUp, Is.True);
            Assert.That(View(Pos(1, 9.5m), Mid(10m), observations: SellSignalEvaluator.MinObservations).IsWarmingUp, Is.False);
            Assert.That(View(Pos(-1, 9.5m), Mid(10m), observations: 0).IsWarmingUp, Is.False, "shorts never get the sell nudge");
            Assert.That(View(Pos(1, 9.5m), Mid(10m), new SellSignal("go", 1m, 1m, 1, 50), observations: 0).IsWarmingUp, Is.False, "a live signal ends warm-up");
        });
    }
}
