using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Evidence.Tests;

/// <summary>Amendment 12 (IP-T625): per-session spread multipliers and re-pricing a simulated trade. 2026-07-17 is EDT.</summary>
public class GapperQuotedSpreadTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static DateTime Et(int h, int m) => MinuteBarMath.ToUtc(Day, new TimeSpan(h, m, 0));
    private static readonly GapperSessionMultipliers Both = new(20, 40);
    private static GapperCostInputs Inputs(double? cs = 0.01) => new(cs, 9.39, 1e12, 0.05, 2.0, 1000, 1.0, 0.0);

    [Test]
    public void For_SplitsAt0930Et_AndIsNullOutsideBothSessions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Both.For(Et(4, 0)), Is.EqualTo(20));
            Assert.That(Both.For(Et(9, 29)), Is.EqualTo(20));
            Assert.That(Both.For(Et(9, 30)), Is.EqualTo(40));
            Assert.That(Both.For(Et(15, 59)), Is.EqualTo(40));
            Assert.That(Both.For(Et(3, 59)), Is.Null);
            Assert.That(Both.For(Et(16, 0)), Is.Null);
        });
    }

    [Test]
    public void RoundTrip_IsTheMeanOfTheLegs_AndNullWhenASessionWasRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Both.RoundTrip(Et(8, 30), Et(9, 15)), Is.EqualTo(20));
            Assert.That(Both.RoundTrip(Et(8, 30), Et(10, 0)), Is.EqualTo(30));
            Assert.That(new GapperSessionMultipliers(20, null).RoundTrip(Et(8, 30), Et(10, 0)), Is.Null);
        });
    }

    [Test]
    public void Reprice_KeepsTheExit_AndChargesCsTimesTheSessionMean()
    {
        var trade = new GapperRuleTrade(Et(8, 30), 10m, Et(10, 0), 11m, "sell-by", 0.10, 0.02, 0.08, false);
        var repriced = GapperQuotedSpread.Reprice(trade, Inputs(), Both)!;
        Assert.Multiple(() =>
        {
            Assert.That(repriced.ExitUtc, Is.EqualTo(trade.ExitUtc));
            Assert.That(repriced.Exit, Is.EqualTo(trade.Exit));
            Assert.That(repriced.RoundTripCost, Is.EqualTo(0.01 * 30).Within(1e-5), "spread cs x (20 + 40) / 2; impact ~0 on huge volume");
            Assert.That(repriced.NetReturn, Is.EqualTo(0.10 - repriced.RoundTripCost).Within(1e-12));
        });
    }

    [Test]
    public void Reprice_IsNull_WhenALegFallsOutsideTheSessions()
    {
        var trade = new GapperRuleTrade(Et(8, 30), 10m, Et(16, 5), 11m, "session-end", 0.10, 0.02, 0.08, false);
        Assert.That(GapperQuotedSpread.Reprice(trade, Inputs(), Both), Is.Null);
    }
}
