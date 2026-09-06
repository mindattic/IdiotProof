using IdiotProof.UI.Components.Options;

namespace IdiotProof.UI.Tests;

/// <summary>
/// Every jargon key the Options components reference must resolve, and every entry must carry
/// both a hover-sized hint and a full explanation. A typo in a component renders a visible
/// "(no glossary entry …)" stub rather than throwing, so this is the test that catches it.
/// </summary>
[TestFixture]
public class OptionsGlossaryTests
{
    /// <summary>Keys used by OptionsChainView, OptionOrderTicket, OptionPositionTracker and Options.razor.</summary>
    private static readonly string[] ReferencedKeys =
    [
        "call", "put", "strike", "premium", "contract", "expiration", "dte", "real", "hype", "hype-meter",
        "breakeven", "iv", "iv-source", "model", "bid-ask", "mid", "limit", "market",
        "buy-to-open", "sell-to-close", "sell-to-open", "buy-to-close", "assignment", "level", "risk-free",
        "moneyness", "sell-signal", "sandbox", "paper", "live", "pnl", "avg", "now", "qty",
    ];

    [TestCaseSource(nameof(ReferencedKeys))]
    public void ReferencedKey_Exists_WithHintAndExplanation(string key)
    {
        Assert.That(OptionsGlossary.TryGet(key, out var entry), Is.True, $"missing glossary entry '{key}'");
        Assert.Multiple(() =>
        {
            Assert.That(entry.Title, Is.Not.Empty);
            Assert.That(entry.Short, Is.Not.Empty.And.Length.LessThanOrEqualTo(160), "hover hint must stay tooltip-sized");
            Assert.That(entry.Long, Is.Not.Empty.And.Length.GreaterThan(entry.Short.Length / 2));
        });
    }

    [Test]
    public void Get_UnknownKey_ReturnsVisibleStub_NotException()
    {
        var e = OptionsGlossary.Get("no-such-term");
        Assert.That(e.Short, Does.Contain("no-such-term"));
    }

    [Test]
    public void Hint_IsTheShortText() =>
        Assert.That(OptionsGlossary.Hint("hype"), Is.EqualTo(OptionsGlossary.Get("hype").Short));

    [TestCase("buy_to_open", "buy-to-open")]
    [TestCase("sell_to_close", "sell-to-close")]
    [TestCase("sell_to_open", "sell-to-open")]
    [TestCase("buy_to_close", "buy-to-close")]
    public void EveryAlpacaIntent_MapsToAGlossaryEntry(string intent, string key)
    {
        Assert.That(OptionsGlossary.IntentKey(intent), Is.EqualTo(key));
        Assert.That(OptionsGlossary.TryGet(key, out _), Is.True);
    }

    [Test]
    public void IntentLabel_SpeaksPlainEnglish_NeverTheRawCode()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OptionsGlossary.IntentLabel("buy_to_open", 0m), Does.Contain("new position"));
            Assert.That(OptionsGlossary.IntentLabel("sell_to_close", 2m), Does.Contain("Closes 2"));
            Assert.That(OptionsGlossary.IntentLabel("sell_to_open", 0m), Does.Contain("SHORT").And.Contain("writing"));
            Assert.That(OptionsGlossary.IntentLabel("buy_to_close", -3m), Does.Contain("3 you wrote"));
            foreach (var code in new[] { "buy_to_open", "sell_to_close", "sell_to_open", "buy_to_close" })
                Assert.That(OptionsGlossary.IntentLabel(code, 1m), Does.Not.Contain("_"), "no snake_case leaks to the screen");
        });
    }

    [Test]
    public void LevelChip_TracksSharedLevelSemantics()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OptionsGlossary.LevelChip(0), Does.Contain("not approved"));
            Assert.That(OptionsGlossary.LevelChip(1), Does.Contain("covered"));
            Assert.That(OptionsGlossary.LevelChip(2), Does.Contain("long"));
            Assert.That(OptionsGlossary.LevelChip(3), Does.Contain("spreads"));
        });
    }
}
