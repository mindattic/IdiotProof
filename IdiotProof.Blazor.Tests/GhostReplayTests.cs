using Bunit;
using IdiotProof.Blazor.Components.Shared;
using IdiotProof.Blazor.Services;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;

namespace IdiotProof.Blazor.Tests;

/// <summary>Ghost replay panel and its chart payload (IP-US-G2..G4, IP-T388).</summary>
public class GhostReplayTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    private static Candle Bar(int minute, decimal close) => new()
    {
        Symbol = "TST", StartUtc = T0.AddMinutes(minute), EndUtc = T0.AddMinutes(minute + 1),
        Open = close, High = close + 0.1m, Low = close - 0.1m, Close = close, Volume = 1000,
    };

    private static (GhostTimeline Ghost, List<Candle> Candles) Run()
    {
        var def = Stock.Ticker("TST")
            .IsPriceBetween(1, 100)
            .IsPriceBetween(20, 100)
            .Then(b => b.StopLoss(5).TakeProfit(30))
            .Else(b => b.StopLoss(9).TakeProfit(11))
            .Build();
        var candles = new List<Candle> { Bar(0, 25m), Bar(1, 25m), Bar(2, 10.6m), Bar(3, 10.8m), Bar(4, 21m) };
        return (GhostTimeline.Run(def, candles), candles);
    }

    [Test]
    public void Payload_DrawsTakenLevelsSolid_UntakenDashed_AndForkHoverLines()
    {
        var (ghost, candles) = Run();
        var payload = GhostChart.Payload(ghost, candles, minRunLength: 1);

        Assert.That(payload.Forks, Has.Count.EqualTo(3));
        var first = payload.Forks[0];
        Assert.That(first.Lines.Any(l => l.StartsWith("▶ If") && l.Contains("pass")), Is.True, string.Join("\n", first.Lines));
        Assert.That(first.Lines.Any(l => l.StartsWith("· Else") && l.Contains("stop 9")), Is.True);
        Assert.That(first.Lines[^1], Does.Contain("Price 25"));

        var atFirst = payload.Levels.Where(l => l.From == payload.Bars[0].Time).ToList();
        Assert.That(atFirst.Single(l => l.Label == "If stop").Dashed, Is.False);
        Assert.That(atFirst.Single(l => l.Label == "Else stop").Dashed, Is.True);
        Assert.That(atFirst.Single(l => l.Label == "If stop").To, Is.EqualTo(payload.Bars[2].Time), "runs until the next fork");
        Assert.That(payload.StopPath, Is.Not.Empty);
        Assert.That(payload.Markers.Count(m => m.Shape == "square"), Is.EqualTo(3), "one fork marker per fork");
    }

    [Test]
    public void Controls_StepThroughFrames_AndRedrawTheGhostAtTheNewBar()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var (ghost, candles) = Run();
        var payload = GhostChart.Payload(ghost, candles);

        var cut = ctx.Render<GhostReplay>(p => p.Add(c => c.Timeline, ghost).Add(c => c.Payload, payload));

        Assert.That(cut.Find(".ghost-position").TextContent, Does.Contain("Bar 1/5"));
        Assert.That(cut.Find(".ghost-back").HasAttribute("disabled"), Is.True);
        Assert.That(cut.FindAll(".ghost-speed option").Select(o => o.TextContent), Is.EqualTo(new[] { "1×", "2×", "5×", "10×", "30×" }));

        cut.Find(".ghost-step").Click();
        cut.Find(".ghost-step").Click();
        Assert.That(cut.Instance.Index, Is.EqualTo(2));
        Assert.That(cut.Find(".ghost-position").TextContent, Does.Contain("Bar 3/5"));
        Assert.That(cut.Find(".ghost-branch").TextContent, Does.Contain("Else"), "the Else branch is active at bar 3");

        var draws = ctx.JSInterop.Invocations.Where(i => i.Identifier == "ipChart.ghost").ToList();
        Assert.That(draws.Select(d => (int)d.Arguments[2]!), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(draws[^1].Arguments[1], Is.SameAs(payload));

        cut.Find(".ghost-step").Click();
        cut.Find(".ghost-step").Click();
        Assert.That(cut.Instance.Index, Is.EqualTo(4));
        Assert.That(cut.Find(".ghost-step").HasAttribute("disabled"), Is.True, "nothing past the last bar");
        Assert.That(cut.FindAll(".ghost-event").Select(e => e.TextContent), Has.Some.Contains("End of session"));

        cut.Find(".ghost-back").Click();
        Assert.That(cut.Instance.Index, Is.EqualTo(3));
        cut.Find(".ghost-restart").Click();
        Assert.That(cut.Instance.Index, Is.EqualTo(0));
    }

    [Test]
    public void Play_RunsToTheLastBar_ThenStops()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var (ghost, candles) = Run();
        var cut = ctx.Render<GhostReplay>(p => p.Add(c => c.Timeline, ghost).Add(c => c.Payload, GhostChart.Payload(ghost, candles)));

        cut.Find(".ghost-speed").Change("30");
        cut.Find(".ghost-play").Click();
        Assert.That(cut.Find(".ghost-play").GetAttribute("aria-pressed"), Is.EqualTo("true"));

        // WaitForState, not WaitForAssertion: NUnit records every failed retry of an Assert.That.
        cut.WaitForState(() => cut.Instance.Index == 4 && !cut.Instance.IsPlaying, TimeSpan.FromSeconds(5));
        Assert.That(cut.Find(".ghost-play").TextContent, Does.Contain("Play"));
    }

    [Test]
    public async Task Dispose_ReleasesTheChart()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var (ghost, candles) = Run();
        ctx.Render<GhostReplay>(p => p.Add(c => c.Timeline, ghost).Add(c => c.Payload, GhostChart.Payload(ghost, candles)));
        await ctx.DisposeComponentsAsync();
        Assert.That(ctx.JSInterop.Invocations.Any(i => i.Identifier == "ipChart.dispose"), Is.True);
    }

    // IP-T397
    [Test]
    public void Payload_CollapsesShortRunsIntoOneChopSegment_WithFlipAndBarCountsInTheTooltip()
    {
        var (ghost, candles) = Run();
        var payload = GhostChart.Payload(ghost, candles, minRunLength: 3);

        Assert.That(payload.Forks, Has.Count.EqualTo(1));
        var chop = payload.Forks[0];
        Assert.That(chop.Title, Does.Contain("chop"));
        Assert.That(chop.Lines[0], Does.Contain("2 flip(s) over 5 bars"));
        Assert.That(chop.Lines[1], Does.Contain("If 3 bar(s)").And.Contain("Else 2 bar(s)"));
        Assert.That(chop.Time, Is.EqualTo(payload.Bars[0].Time));
        Assert.That(chop.Until, Is.EqualTo(payload.Bars[^1].Time), "hovering anywhere in the segment shows it");
        Assert.That(payload.Markers.Count(m => m.Shape == "square"), Is.EqualTo(1));
        Assert.That(payload.Levels, Is.Empty, "a chop segment draws no branch levels");
    }

    [Test]
    public void Payload_PlainSegment_SpansItsRun_AndKeepsBranchLevels()
    {
        var (ghost, candles) = Run();
        var payload = GhostChart.Payload(ghost, candles, minRunLength: 1);
        var first = payload.Forks[0];
        Assert.That(first.Until, Is.EqualTo(payload.Bars[1].Time), "the If run covers bars 0..1");
        Assert.That(first.Lines[0], Does.Contain("If for 2 bars"));
    }
}
