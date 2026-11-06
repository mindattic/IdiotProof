using IdiotProof.Core.Services;
using IdiotProof.Models;

namespace IdiotProof.Blazor.Tests;

/// <summary>IP-T443: the voter panel's ballot context gains CATALYST and MOMENTUM sections when the Monitor hands it <see cref="TradeSignalFacts"/>.</summary>
[TestFixture]
public sealed class TradeSignalFactsTests
{
    private static readonly TradeSignal Signal = new()
    {
        Symbol = "ABCD", Direction = TradeDirection.Long, StrategyName = "ABCD Gapper", SuggestedEntry = 3.20m, SuggestedStop = 2.95m,
        Reason = "gap up 30% on FDA news", GeneratedUtc = new DateTime(2026, 11, 6, 13, 30, 0, DateTimeKind.Utc),
    };

    private static string Context(TradeSignalFacts? facts) =>
        LlmVotingService.BuildSignalContext(Signal, [], facts).Replace("\r\n", "\n");

    [Test]
    public void WithoutFacts_TheContextIsUnchanged()
    {
        var text = LlmVotingService.BuildSignalContext(Signal, []);
        Assert.That(text, Does.StartWith("TRADE SIGNAL — ABCD"));
        Assert.That(text, Does.Not.Contain("CATALYST").And.Not.Contain("MOMENTUM"));
        Assert.That(LlmVotingService.BuildSignalContext(Signal, [], null), Is.EqualTo(text));
    }

    [Test]
    public void WithFacts_RendersCatalystAndMomentum_OneLinePerKnownFact()
    {
        var facts = new TradeSignalFacts
        {
            CatalystHeadline = "Acme Receives FDA Approval\nfor ZYX-1\tand more", CatalystScore = 72.4, CatalystAgeMinutes = 37.2, CatalystDirection = 1, CatalystPreClass = "Fda",
            Velocity1m = 0.8, Velocity3m = 0.45, Velocity5m = -0.3, Acceleration = 0.12, VolRatioTod = 6.24, DistFromPmHighPct = 1.8, Halted = false,
        };
        var text = Context(facts);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("CATALYST:\n  Headline: Acme Receives FDA Approval for ZYX-1 and more"), "newlines and tabs collapse to one line");
            Assert.That(text, Does.Contain("score 72/100, direction bullish, type Fda, published 37 min before this signal"));
            Assert.That(text, Does.Contain("MOMENTUM:\n  Velocity: 1m +0.80%/min, 3m +0.45%/min, 5m -0.30%/min"));
            Assert.That(text, Does.Contain("Acceleration (v3 now vs 3 min ago): +0.12"));
            Assert.That(text, Does.Contain("Volume vs time-of-day norm: 6.2x"));
            Assert.That(text, Does.Contain("Below premarket high: 1.8%"));
            Assert.That(text, Does.Contain("Trading status: trading"));
            Assert.That(text.IndexOf("CATALYST:", StringComparison.Ordinal), Is.LessThan(text.IndexOf("MOMENTUM:", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void EmptyFacts_SayNoneKnown_AndAHaltIsShouted()
    {
        var text = Context(new TradeSignalFacts { Halted = true });
        Assert.That(text, Does.Contain("CATALYST:\n  None known since the prior close."));
        Assert.That(text, Does.Contain("Trading status: HALTED"));
        Assert.That(text, Does.Not.Contain("Velocity:"));
        Assert.That(Context(new TradeSignalFacts()), Does.Contain("No formation features available."));
    }

    [Test]
    public void ALongHeadline_IsTruncated()
    {
        var text = Context(new TradeSignalFacts { CatalystHeadline = new string('x', 500) });
        var line = text.Split('\n').Single(l => l.StartsWith("  Headline: "));
        Assert.That(line.Length, Is.EqualTo("  Headline: ".Length + 200));
        Assert.That(line, Does.EndWith("…"));
    }
}
