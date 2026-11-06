using IdiotProof.Core.Monitoring.Formation;

namespace IdiotProof.Blazor.Tests;

/// <summary>IP-T443: the formation universe deduplicates its three legs, honors the price and cap bands, ranks by catalyst then gap, and cuts at the cap.</summary>
[TestFixture]
public sealed class FormationUniverseTests
{
    private static readonly DateTime Published = new(2026, 11, 6, 11, 0, 0, DateTimeKind.Utc);

    private static CatalystSymbolHit Hit(string symbol, double score) => new(symbol, score, 1, Published, "headline", "Fda", 2);

    private static FormationConfig Config(int max = 150, double? capMin = null, double? capMax = null) =>
        new() { MaxSymbols = max, PriceMin = 0.10, PriceMax = 20, MarketCapMin = capMin, MarketCapMax = capMax };

    [Test]
    public void ASymbolNamedByEveryLeg_AppearsOnce_WithEveryReason()
    {
        var members = FormationUniverse.Build(
            [new TrackedTickerInfo("abcd", true, 3.2m, 10_000_000)],
            [Hit("ABCD", 40), Hit("ABCD", 65)],
            [new ScreenerHit("ABCD", 42.5, 3.4)],
            [new ScreenerHit("ABCD")],
            Config());

        var m = members.Single();
        Assert.Multiple(() =>
        {
            Assert.That(m.Symbol, Is.EqualTo("ABCD"));
            Assert.That(m.Reasons, Is.EquivalentTo(new[] { "tracked", "catalyst", "mover", "most-active" }));
            Assert.That(m.CatalystScore, Is.EqualTo(65), "the best catalyst score wins");
            Assert.That(m.GapPct, Is.EqualTo(42.5));
            Assert.That(m.Price, Is.EqualTo(3.4), "the mover's live price beats the cached tracked price");
            Assert.That(m.SharesOutstanding, Is.EqualTo(10_000_000));
            Assert.That(m.MarketCap, Is.EqualTo(34_000_000).Within(0.01));
        });
    }

    [Test]
    public void RanksByCatalystScore_ThenGap_ThenReasons_ThenSymbol_AndCuts()
    {
        var members = FormationUniverse.Build(
            [new TrackedTickerInfo("ZZZZ", true, 1m, null), new TrackedTickerInfo("AAAA", true, 1m, null), new TrackedTickerInfo("MMMM", true, 1m, null)],
            [Hit("CATL", 70), Hit("CATH", 90)],
            [new ScreenerHit("MOVB", 20, 2), new ScreenerHit("MOVA", 55, 2), new ScreenerHit("MMMM", 5, 1)],
            [],
            Config(max: 6));

        Assert.That(members.Select(m => m.Symbol), Is.EqualTo(new[] { "CATH", "CATL", "MOVA", "MOVB", "MMMM", "AAAA" }));
    }

    [Test]
    public void PriceBand_AppliesWhereAPriceIsKnown_AndUnknownPricesStay()
    {
        var members = FormationUniverse.Build(
            [new TrackedTickerInfo("CHEAP", true, 0.05m, null), new TrackedTickerInfo("DEAR", true, 25m, null), new TrackedTickerInfo("OK", true, 4m, null), new TrackedTickerInfo("HALT", false, 4m, null)],
            [Hit("DEAR", 80), Hit("NOPX", 50)],
            [new ScreenerHit("RICH", 30, 40)],
            [],
            Config());

        Assert.That(members.Select(m => m.Symbol), Is.EquivalentTo(new[] { "NOPX", "OK" }));
    }

    [Test]
    public void MarketCapBand_DropsKnownOutliers_AndKeepsUnknownShares()
    {
        var members = FormationUniverse.Build(
            [
                new TrackedTickerInfo("SMALL", true, 2m, 1_000_000),
                new TrackedTickerInfo("BIG", true, 10m, 2_000_000_000),
                new TrackedTickerInfo("MID", true, 5m, 20_000_000),
                new TrackedTickerInfo("UNKN", true, 5m, null),
            ],
            [], [], [],
            Config(capMin: 5_000_000, capMax: 1_000_000_000));

        Assert.That(members.Select(m => m.Symbol), Is.EquivalentTo(new[] { "MID", "UNKN" }));
    }

    [Test]
    public void BlankSymbols_AreIgnored_AndCaseIsNormalized()
    {
        var members = FormationUniverse.Build([new TrackedTickerInfo(" ", true, 1m, null)], [Hit("abcd", 10)], [new ScreenerHit("")], [new ScreenerHit("Abcd")], Config());
        Assert.That(members.Single().Symbol, Is.EqualTo("ABCD"));
        Assert.That(members.Single().Why, Is.EqualTo("catalyst+most-active"));
    }
}
