using Bunit;
using IdiotProof.Core.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.UI.Components.Shared;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The strategy editor's Stamp card shows exactly what <c>idiotproof strategy stamp</c> prints for
/// the saved canon, in a monospace block that keeps every column where the CLI put it.
/// </summary>
[TestFixture]
public sealed class StrategyStampViewTests
{
    private static StrategyDefinition Sample() => Stock.Ticker("AAPL")
        .Name("VWAP + EMA trend long")
        .IsAboveVwap()
        .IsAboveEma(9)
        .Quantity(1)
        .Long()
        .TakeProfit(195.5)
        .StopLoss(188.25)
        .Build();

    [Test]
    public void SavedCanon_RendersTheSameStampAsTheCli()
    {
        var def = Sample();
        var json = StrategyJson.Serialize(def);

        var ascii = StrategyStampView.For(new Strategy { ScriptJson = json, ScriptText = "" });
        var boxes = StrategyStampView.For(json, null, unicode: true, includePayload: false);

        Assert.Multiple(() =>
        {
            Assert.That(ascii.Error, Is.Null);
            Assert.That(ascii.Stamp, Is.EqualTo(def.ToStamp()));
            Assert.That(ascii.ShortId, Is.EqualTo(def.ShortId()));
            Assert.That(boxes.Stamp, Is.EqualTo(def.ToStamp(new StampOptions { Unicode = true, IncludePayload = false })));
            Assert.That(ascii.Stamp!.Split('\n').Max(l => l.TrimEnd('\r').Length), Is.LessThanOrEqualTo(StrategyStamp.Width));
        });
    }

    [Test]
    public void UnsavedOrUnreadable_SaysWhyThereIsNoStamp()
    {
        var unsaved = StrategyStampView.For((Strategy?)null);
        var broken = StrategyStampView.For("{ not json", null);

        Assert.Multiple(() =>
        {
            Assert.That(unsaved.Stamp, Is.Null);
            Assert.That(unsaved.Error, Does.Contain("Save"));
            Assert.That(broken.Stamp, Is.Null);
            Assert.That(broken.Error, Does.StartWith("No stamp"));
        });
    }

    [Test]
    public void AsciiBlock_RendersTheTextVerbatim_InANonWrappingMonospacePre()
    {
        var stamp = Sample().ToStamp();
        using var ctx = new BunitContext();

        var block = ctx.Render<AsciiBlock>(p => p.Add(b => b.Text, stamp).Add(b => b.AriaLabel, "Strategy stamp"));
        var pre = block.Find("pre");

        Assert.Multiple(() =>
        {
            Assert.That(pre.TextContent, Is.EqualTo(stamp), "every space and line break kept");
            var style = pre.GetAttribute("style") ?? "";
            Assert.That(style, Does.Contain("white-space:pre;"));
            Assert.That(style, Does.Contain("monospace"));
            Assert.That(style, Does.Contain("font-variant-ligatures:none"));
            Assert.That(pre.GetAttribute("aria-label"), Is.EqualTo("Strategy stamp"));
        });
    }
}
