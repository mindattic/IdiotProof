using Bunit;
using IdiotProof.Blazor.Components.Shared;
using IdiotProof.Blazor.Services;
using IdiotProof.Models;
using IdiotProof.Strategies.Backtesting;

namespace IdiotProof.Blazor.Tests;

/// <summary>IP-US-G1: the strategy price chart (Lightweight Charts through Chart.razor).</summary>
public sealed class ChartTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    private static Candle Bar(int minute, decimal close, decimal? open = null) => new()
    {
        Symbol = "ABCD",
        StartUtc = T0.AddMinutes(minute),
        EndUtc = T0.AddMinutes(minute + 1),
        Open = open ?? close,
        High = Math.Max(open ?? close, close) + 0.01m,
        Low = Math.Min(open ?? close, close) - 0.01m,
        Close = close,
        Volume = 1000,
    };

    [Test]
    public void Bars_AreSortedUnique_AndDropInvalidBars_BecauseTheLibraryThrowsOtherwise()
    {
        var bad = new Candle { Symbol = "ABCD", StartUtc = T0.AddMinutes(5), Open = 2, High = 1, Low = 3, Close = 2 };
        var bars = ChartSeries.Bars([Bar(2, 2.2m), Bar(0, 2.0m), Bar(1, 2.1m), Bar(1, 2.15m), bad]);

        Assert.That(bars.Select(b => b.Time), Is.Ordered.And.Unique);
        Assert.That(bars, Has.Count.EqualTo(3));
        Assert.That(bars[1].Close, Is.EqualTo(2.15m), "a duplicate time keeps the last bar");
        Assert.That(bars[0].Time, Is.EqualTo(new DateTimeOffset(T0).ToUnixTimeSeconds()));
    }

    [Test]
    public void Markers_SnapEntriesExitsAndTriggers_OntoBars_InTimeOrder()
    {
        var report = new BacktestReport { Symbol = "ABCD" };
        var trade = new BacktestTrade { Direction = TradeDirection.Long, EntryUtc = T0.AddMinutes(1).AddSeconds(30), EntryPrice = 2.10m, Quantity = 1 };
        trade.Exits.Add(new BacktestFill { Utc = T0.AddMinutes(3), Price = 2.30m, Reason = "target" });
        report.Trades.Add(trade);
        report.Triggers.Add(new TriggerFire { Utc = T0, Condition = "IsAboveVwap", Price = 2.0m });

        var payload = ChartSeries.Payload("ABCD", [Bar(0, 2.0m), Bar(1, 2.1m), Bar(2, 2.2m), Bar(3, 2.3m)], report);

        Assert.That(payload.Markers.Select(m => m.Time), Is.Ordered);
        Assert.That(payload.Markers.Select(m => m.Time), Is.SubsetOf(payload.Bars.Select(b => b.Time)));
        var entry = payload.Markers.Single(m => m.Text.StartsWith("Buy"));
        Assert.That(entry.Time, Is.EqualTo(payload.Bars[1].Time), "a mid-bar entry lands on the bar that contains it");
        Assert.That(payload.Markers.Single(m => m.Text.StartsWith("Exit")).Text, Does.Contain("target"));
        Assert.That(payload.Markers.Any(m => m.Text == "IsAboveVwap"), Is.True);
    }

    [Test]
    public void Markers_WithoutAReport_AreEmpty()
    {
        Assert.That(ChartSeries.Payload("ABCD", [Bar(0, 2m)]).Markers, Is.Empty);
    }

    [Test]
    public async Task Chart_HandsThePayloadToLightweightCharts_OnceAndDisposesIt()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var payload = ChartSeries.Payload("ABCD", [Bar(0, 2.0m), Bar(1, 2.1m)]);

        var cut = ctx.Render<Chart>(p => p.Add(c => c.Payload, payload));
        cut.Render();

        var renders = ctx.JSInterop.Invocations.Where(i => i.Identifier == "ipChart.render").ToList();
        Assert.That(renders, Has.Count.EqualTo(1), "re-rendering the same payload doesn't redraw");
        Assert.That(renders[0].Arguments[1], Is.SameAs(payload));
        Assert.That(cut.Find(".ip-chart").GetAttribute("aria-label"), Is.EqualTo("ABCD price chart, 2 bars"));

        await ctx.DisposeComponentsAsync();
        Assert.That(ctx.JSInterop.Invocations.Any(i => i.Identifier == "ipChart.dispose"), Is.True);
    }
}
