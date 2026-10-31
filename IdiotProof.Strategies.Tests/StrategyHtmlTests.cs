using System.Text.RegularExpressions;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// IP-T15: <see cref="StrategyHtml"/> ToHtml / ToMermaid / ToSvg on representative strategies (a gapper
/// profile, a multi-phase strategy with conditional blocks, one with no exits). It renders user-authored
/// strategy text into pages (the replay archive, the Blazor UI), so its escaping is an injection boundary.
/// </summary>
[TestFixture]
public sealed class StrategyHtmlTests
{
    /// <summary>A condition whose script text is whatever the test says (user-controlled text stand-in).</summary>
    private sealed class TextCondition(string script, StrategyPhase phase = StrategyPhase.Entry) : ICondition
    {
        public string ToScript() => script;
        public bool Evaluate(IndicatorSnapshot indicators) => false;
        public StrategyPhase Phase => phase;
    }

    private const string Hostile = "<img src=x onerror=\"alert('x')\"> & co";

    [Test]
    public void ToHtml_EncodesConditionScriptText()
    {
        // IP-T340: Filters/Entry card rows were emitted raw while Branch rows were encoded.
        var def = new StrategyDefinition { Symbol = "ABC", Quantity = 1 };
        def.EntryConditions.Add(new TextCondition(Hostile, StrategyPhase.Filters));
        def.EntryConditions.Add(new TextCondition(Hostile));

        var html = def.ToHtml();

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("<img"));
            Assert.That(html, Does.Not.Contain("\"alert"));
            Assert.That(html, Does.Contain("&lt;img src=x onerror=&quot;alert(&#39;x&#39;)&quot;&gt; &amp; co"));
        });
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    /// <summary>A premarket gapper profile: entry window filter, gap and price bands, stop/trail/giveback/sell-by.</summary>
    private static StrategyDefinition Gapper()
    {
        var def = new StrategyDefinition
        {
            Symbol = "GAPR", Session = TradingSession.Premarket, Quantity = 1,
            StopLossPercent = 8, TrailingStopPercent = 5, TakeProfitPercent = 25,
            PeakGivebackPercent = 30, PeakGivebackArmTime = new TimeSpan(7, 0, 0), ExitTime = new TimeSpan(9, 25, 0),
        };
        def.EntryConditions.Add(new TimeWindowCondition(new TimeSpan(4, 0, 0), new TimeSpan(9, 0, 0)));
        def.EntryConditions.Add(new GapBandCondition(15, 60));
        def.EntryConditions.Add(new PriceBandCondition(1, 20));
        return def;
    }

    /// <summary>A short, notionally sized strategy with an If / ElseIf / Else conditional block and price targets.</summary>
    private static StrategyDefinition Branching()
    {
        var def = new StrategyDefinition
        {
            Symbol = "BRCH", Direction = TradeDirection.Short, NotionalAmount = 500m,
            StopLossPrice = 12.5, TakeProfitPrice = 9,
        };
        def.EntryConditions.Add(new PriceCondition(ConditionType.Entry, 10));
        def.TakeProfitTargets.Add(new TakeProfitTarget { Label = "T1", Price = 9.5, PercentToSell = 50 });
        var block = new ConditionalBlock();
        block.Branches.Add(new ConditionalBranch { Condition = new PriceCondition(ConditionType.Entry, 11), Overrides = { StopLossPercent = 3 } });
        block.Branches.Add(new ConditionalBranch { Condition = new GapBandCondition(5, 10), Overrides = { TrailingStopPercent = 2 } });
        block.Branches.Add(new ConditionalBranch { Condition = null, Overrides = { Direction = TradeDirection.Long } });
        def.ConditionalBlocks.Add(block);
        return def;
    }

    /// <summary>No conditions, no stop, no exits at all.</summary>
    private static StrategyDefinition Bare() => new() { Symbol = "BARE", Quantity = 3 };

    private static int Count(string haystack, string needle) => Regex.Matches(haystack, Regex.Escape(needle)).Count;

    // ── ToHtml ──────────────────────────────────────────────────────────────────

    [Test]
    public void ToHtml_Gapper_HasEveryPhaseCard_WithFiltersSplitFromEntry()
    {
        var html = Gapper().ToHtml();
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.StartWith("<div class=\"phases\">").And.EndWith("</div>"));
            foreach (var phase in new[] { "setup", "filters", "entry", "order", "risk", "exit" })
                Assert.That(html, Does.Contain($"data-phase=\"{phase}\""), phase);
            Assert.That(html, Does.Not.Contain("data-phase=\"branch\""));
            Assert.That(Count(html, "class=\"phase\""), Is.EqualTo(6));
            Assert.That(html, Does.Contain("RequireEntryWindow(&quot;04:00&quot;, &quot;09:00&quot;)"), "filter text is encoded");
            Assert.That(html, Does.Contain("IsGapBetween(15, 60)"));
            Assert.That(html, Does.Contain("IsPriceBetween(1, 20)"));
            Assert.That(html, Does.Contain("<span class=\"v\">GAPR</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">Premarket</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">1 shares</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">Buy long</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">8%</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">5% off peak</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">25%</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">30% armed 07:00 ET</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">09:25 ET</span>"));
            Assert.That(html, Does.Not.Contain("no stop"));
        });
    }

    [Test]
    public void ToHtml_Branching_RendersIfElseIfElseRows_ShortSide_NotionalSize_AndTargets()
    {
        var html = Branching().ToHtml();
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("data-phase=\"branch\""));
            Assert.That(html, Does.Contain("<b>If</b> Entry(11) → StopLossPercent(3)"));
            Assert.That(html, Does.Contain("<b>ElseIf</b> IsGapBetween(5, 10) → TrailingStopLoss(2)"));
            Assert.That(html, Does.Contain("<b>Else</b> (default) → Long()"));
            Assert.That(html, Does.Not.Contain("data-phase=\"filters\""), "no filter conditions, no Filters card");
            Assert.That(html, Does.Contain("<span class=\"v\">Sell short</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">$500 notional</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">$500</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">$12.5</span>"));
            Assert.That(html, Does.Contain("<span class=\"k\">Target T1</span><span class=\"v\">$9.5 &#183; sell 50%</span>"));
            Assert.That(html, Does.Contain("<span class=\"v\">$9</span>"));
        });
    }

    [Test]
    public void ToHtml_EmptyExits_WarnsAboutTheMissingStop_AndSaysStopOnly()
    {
        var html = Bare().ToHtml();
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("no stop — RiskGuardian would veto"));
            Assert.That(html, Does.Contain("(no target/time exit — stop only)"));
            Assert.That(html, Does.Not.Contain("data-phase=\"entry\""));
            Assert.That(html, Does.Contain("<span class=\"v\">3 shares</span>"));
        });
    }

    [Test]
    public void ToHtml_EncodesSymbolAndTargetLabels()
    {
        var def = Bare();
        def.Symbol = "<b>A&B\"</b>";
        def.TakeProfitTargets.Add(new TakeProfitTarget { Label = "<i>T1</i>", Price = 5 });
        var html = def.ToHtml();
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("<b>A"));
            Assert.That(html, Does.Not.Contain("<i>"));
            Assert.That(html, Does.Contain("&lt;b&gt;A&amp;B&quot;&lt;/b&gt;"));
            Assert.That(html, Does.Contain("Target &lt;i&gt;T1&lt;/i&gt;"));
        });
    }

    [Test]
    public void ToHtml_EncodesBranchConditionAndOverrideText()
    {
        var def = Bare();
        var block = new ConditionalBlock();
        var overrides = new StrategyOverrides();
        overrides.EntryConditions.Add(new TextCondition("<script>x()</script>"));
        block.Branches.Add(new ConditionalBranch { Condition = new TextCondition(Hostile), Overrides = overrides });
        def.ConditionalBlocks.Add(block);

        var html = def.ToHtml();
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("<img"));
            Assert.That(html, Does.Not.Contain("<script"));
            Assert.That(html, Does.Contain("&lt;script&gt;x()&lt;/script&gt;"));
        });
    }

    // ── ToMermaid ───────────────────────────────────────────────────────────────

    /// <summary>Every quoted node label: <c>id["text"]</c> or <c>id{"text"}</c>.</summary>
    private static List<string> MermaidLabels(string mermaid) =>
        Regex.Matches(mermaid, "^\\s*\\w+[\\[{]\"(.*)\"[\\]}]\\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();

    [Test]
    public void ToMermaid_Gapper_ChainsEveryConditionIntoTheGateAndExit()
    {
        var m = Gapper().ToMermaid();
        Assert.Multiple(() =>
        {
            Assert.That(m, Does.StartWith("flowchart TD"));
            Assert.That(m, Does.Contain("setup[\"Setup - GAPR - Premarket\"]"));
            Assert.That(m, Does.Contain("c0[\"RequireEntryWindow 04:00 09:00\"]"));
            Assert.That(m, Does.Contain("c1[\"IsGapBetween 15 60\"]"));
            Assert.That(m, Does.Contain("c2[\"IsPriceBetween 1 20\"]"));
            Assert.That(m, Does.Contain("setup --> c0"));
            Assert.That(m, Does.Contain("c0 --> c1"));
            Assert.That(m, Does.Contain("c2 --> gate"));
            Assert.That(m, Does.Contain("gate{\"ALL conditions true?\"}"));
            Assert.That(m, Does.Contain("gate -->|yes| llm"));
            Assert.That(m, Does.Contain("gate -->|no| wait"));
            Assert.That(m, Does.Contain("order[\"Order - BUY long - 1 shares\"]"));
            Assert.That(m, Does.Contain("exit[\"Exit - stop 8pct / trail 5pct / giveback 30pct / sell-by 09:25\"]"));
            Assert.That(m, Does.Contain("order --> exit"));
        });
    }

    [Test]
    public void ToMermaid_BareStrategy_FiresInSession_AndExitsManually()
    {
        var m = Bare().ToMermaid();
        Assert.Multiple(() =>
        {
            Assert.That(m, Does.Contain("gate{\"no conditions - fires in-session\"}"));
            Assert.That(m, Does.Contain("setup --> gate"));
            Assert.That(m, Does.Contain("exit[\"Exit - manual only\"]"));
            Assert.That(m, Does.Not.Contain("c0"));
        });
    }

    [Test]
    public void ToMermaid_ShortNotional()
    {
        var m = Branching().ToMermaid();
        Assert.Multiple(() =>
        {
            Assert.That(m, Does.Contain("order[\"Order - SELL short - 500 notional\"]"));
            Assert.That(m, Does.Contain("exit[\"Exit - stop 12.5 / target 9\"]"));
        });
    }

    [Test]
    public void ToMermaid_LabelsKeepOnlyTheSafeCharacterSet()
    {
        var def = Gapper();
        def.Symbol = "A\"]--> evil[\"x|y;z{w}(v)<b>&é";
        def.EntryConditions.Add(new TextCondition(Hostile));
        var m = def.ToMermaid();

        var labels = MermaidLabels(m);
        Assert.Multiple(() =>
        {
            Assert.That(labels, Has.Count.EqualTo(Count(m, "\"]") + Count(m, "\"}")), "every label parsed");
            foreach (var label in labels)
                Assert.That(label, Does.Match("^[A-Za-z0-9 .%$/:?-]*$"), label);
            Assert.That(m, Does.Not.Contain("evil[\""), "the symbol cannot open a new node");
            Assert.That(m, Does.Contain("setup[\"Setup - A -- evil x y z w v b - Premarket\"]"));
        });
    }

    // ── ToSvg (WebUtility.HtmlEncode writes the middle dot as &#183;) ───────────────────────────────────────────────────────────────────

    [Test]
    public void ToSvg_Gapper_OneNodePerStep_OneDiamond_ArrowsBetween()
    {
        var svg = Gapper().ToSvg();
        // setup + 3 conditions + decision + 2 gates + order + exit = 9 nodes.
        Assert.Multiple(() =>
        {
            Assert.That(svg, Does.StartWith("<svg").And.EndWith("</svg>"));
            Assert.That(Count(svg, "<rect"), Is.EqualTo(8));
            Assert.That(Count(svg, "<polygon"), Is.EqualTo(1));
            Assert.That(Count(svg, "<line"), Is.EqualTo(8));
            Assert.That(Count(svg, "<text"), Is.EqualTo(9));
            Assert.That(svg, Does.Contain(">Setup &#183; GAPR &#183; Premarket</text>"));
            Assert.That(svg, Does.Contain(">RequireEntryWindow(&quot;04:00&quot;, &quot;09:00&quot;)</text>"));
            Assert.That(svg, Does.Contain(">ALL conditions true?</text>"));
            Assert.That(svg, Does.Contain(">Order &#183; BUY long</text>"));
            Assert.That(svg, Does.Contain(">Exit &#183; stop 8% / trail 5% / giveback 30% / …</text>"), "labels over 44 chars are cut to 43 + ellipsis");
        });
    }

    [Test]
    public void ToSvg_BareStrategy_ManualExit_AndUniqueMarkerIds()
    {
        var a = Bare().ToSvg();
        var b = Bare().ToSvg();
        static string Marker(string svg) => Regex.Match(svg, "marker id=\"(\\w+)\"").Groups[1].Value;
        Assert.Multiple(() =>
        {
            Assert.That(a, Does.Contain(">Exit &#183; manual only</text>"));
            Assert.That(Count(a, "<rect"), Is.EqualTo(5), "setup, two gates, order, exit");
            Assert.That(Marker(a), Is.Not.Empty);
            Assert.That(Marker(a), Is.Not.EqualTo(Marker(b)), "two diagrams on one page must not share a marker id");
            Assert.That(a, Does.Contain($"url(#{Marker(a)})"));
        });
    }

    [Test]
    public void ToSvg_EncodesSymbolAndConditionText()
    {
        var def = Bare();
        def.Symbol = "<b>&";
        def.EntryConditions.Add(new TextCondition("<x a='1'>"));
        var svg = def.ToSvg();
        Assert.Multiple(() =>
        {
            Assert.That(svg, Does.Not.Contain("<b>"));
            Assert.That(svg, Does.Not.Contain("<x "));
            Assert.That(svg, Does.Contain("Setup &#183; &lt;b&gt;&amp; &#183; RTH"));
            Assert.That(svg, Does.Contain("&lt;x a=&#39;1&#39;&gt;"));
        });
    }
}
