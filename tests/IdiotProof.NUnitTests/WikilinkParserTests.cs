using IdiotProof.Blazor.Services;

namespace IdiotProof.NUnitTests;

/// <summary>
/// WikilinkParser is the bridge from documentation prose → live-rendered
/// strategies. These tests cover: simple text passthrough, single-wikilink
/// extraction, multi-wikilink interleaving, and unparseable scripts surfacing
/// as fallback tokens (not silently dropped).
/// </summary>
[TestFixture]
public class WikilinkParserTests
{
    [Test]
    public void Parse_PlainText_ReturnsSingleTextToken()
    {
        var tokens = WikilinkParser.Parse("Just plain prose, no links.");
        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].TokenKind, Is.EqualTo(WikilinkParser.WikilinkToken.Kind.Text));
    }

    [Test]
    public void Parse_SingleWikilink_ReturnsTextStrategyText()
    {
        var input = "Before [[Stock.Ticker(\"AAPL\").Long().Build()]] after.";
        var tokens = WikilinkParser.Parse(input);

        Assert.That(tokens, Has.Count.EqualTo(3));
        Assert.That(tokens[0].TokenKind, Is.EqualTo(WikilinkParser.WikilinkToken.Kind.Text));
        Assert.That(tokens[1].TokenKind, Is.EqualTo(WikilinkParser.WikilinkToken.Kind.Strategy));
        Assert.That(tokens[1].Strategy, Is.Not.Null);
        Assert.That(tokens[1].Strategy!.Symbol, Is.EqualTo("AAPL"));
        Assert.That(tokens[2].TokenKind, Is.EqualTo(WikilinkParser.WikilinkToken.Kind.Text));
    }

    [Test]
    public void Parse_MultipleWikilinks_AllExtracted()
    {
        var input = "[[Stock.Ticker(\"A\").Long().Build()]] then [[Stock.Ticker(\"B\").Short().Build()]]";
        var tokens = WikilinkParser.Parse(input);

        var strategies = tokens.Where(t => t.TokenKind == WikilinkParser.WikilinkToken.Kind.Strategy).ToList();
        Assert.That(strategies, Has.Count.EqualTo(2));
        Assert.That(strategies[0].Strategy!.Symbol, Is.EqualTo("A"));
        Assert.That(strategies[1].Strategy!.Symbol, Is.EqualTo("B"));
    }

    [Test]
    public void Parse_UnparseableScript_ReturnsFallbackToken()
    {
        var tokens = WikilinkParser.Parse("[[just nonsense]]");
        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].TokenKind, Is.EqualTo(WikilinkParser.WikilinkToken.Kind.UnparseableScript));
        Assert.That(tokens[0].Error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void ParseScript_FullChain_ProducesPopulatedDefinition()
    {
        var def = WikilinkParser.ParseScript(
            "Stock.Ticker(\"NVDA\").RequireAdxAbove(20).IsAboveVwap().IsBetweenEma(9, 31).OnReclaim(9).Long().StopLoss(450).TakeProfit(485).Build()");

        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Symbol, Is.EqualTo("NVDA"));
        Assert.That(def.EntryConditions, Is.Not.Empty);
        Assert.That(def.StopLossPrice, Is.EqualTo(450));
        Assert.That(def.TakeProfitPrice, Is.EqualTo(485));
    }

    [Test]
    public void ParseScript_EmptyOrNoTicker_ReturnsNull()
    {
        Assert.That(WikilinkParser.ParseScript(""), Is.Null);
        Assert.That(WikilinkParser.ParseScript("not a real script"), Is.Null);
    }
}
