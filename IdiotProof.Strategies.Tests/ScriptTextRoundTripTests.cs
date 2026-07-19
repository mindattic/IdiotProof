using System.Globalization;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// IP-A19 regressions: the human-view script text must serialize losslessly
/// regardless of host locale, and view generation must not crash on
/// canon-legal shapes.
/// </summary>
public class ScriptTextRoundTripTests
{
    [Test]
    public void ToScript_CommaDecimalLocale_RoundTripsFractionalNumbers()
    {
        // On a de-DE host, TrailingStopLoss(2.5) used to serialize as
        // "TrailingStopLoss(2,5)" — which the invariant-culture parser reads
        // as TWO args and applies as a 2% trail: a silently tightened stop.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var builder = Stock.Ticker("TST")
                .IsGapUp(7.5)
                .Long().Quantity(10)
                .StopLossPercent(1.5)
                .TrailingStopLoss(2.5);

            var script = builder.ToScript();
            Assert.That(script, Does.Contain("TrailingStopLoss(2.5)"), "invariant decimal point");

            var parsed = ScriptParser.ParseScript(script);
            Assert.Multiple(() =>
            {
                Assert.That(parsed!.TrailingStopPercent, Is.EqualTo(2.5));
                Assert.That(parsed.StopLossPercent, Is.EqualTo(1.5));
            });
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Test]
    public void OverridesToScript_CommaDecimalLocale_UsesInvariantNumbers()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var overrides = new StrategyOverrides { TakeProfitPrice = 5.5, StopLossPercent = 1.25 };
            var script = overrides.ToScript();
            Assert.Multiple(() =>
            {
                Assert.That(script, Does.Contain("TakeProfit(5.5)"));
                Assert.That(script, Does.Contain("StopLossPercent(1.25)"));
            });
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Test]
    public void ConditionalBlock_ToScript_ElseOnlyBlock_DoesNotThrow()
    {
        // Canonical JSON legally permits a null condition on ANY branch
        // (including the first); the view generator used to bang-dereference
        // the first branch's condition.
        var block = new ConditionalBlock();
        block.Branches.Add(new ConditionalBranch
        {
            Condition = null,
            Overrides = new StrategyOverrides { Direction = TradeDirection.Short },
        });

        string? script = null;
        Assert.DoesNotThrow(() => script = block.ToScript());
        Assert.That(script, Does.Contain(".Else("));
    }
}
