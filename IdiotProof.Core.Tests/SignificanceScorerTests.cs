using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;

namespace IdiotProof.Blazor.Tests;

[TestFixture]
public class SignificanceScorerTests
{
    [TestCase("High", 100)]
    [TestCase("Medium", 60)]
    [TestCase("Low", 20)]
    [TestCase("Garbage", 20)]
    public void MagnitudeScore_MapsKnownAndUnknownValues(string magnitude, double expected)
        => Assert.That(SignificanceScorer.MagnitudeScore(magnitude), Is.EqualTo(expected));

    [Test]
    public void ConfidenceMultiplier_AlreadyHappened_IsAlwaysFullConfidence()
        => Assert.That(SignificanceScorer.ConfidenceMultiplier(true, "Low"), Is.EqualTo(1.0));

    [TestCase("High", 1.0)]
    [TestCase("Medium", 0.7)]
    [TestCase("Low", 0.4)]
    [TestCase(null, 0.85)]
    public void ConfidenceMultiplier_PendingPortent_MapsTriggerConfidence(string? confidence, double expected)
        => Assert.That(SignificanceScorer.ConfidenceMultiplier(false, confidence), Is.EqualTo(expected));

    [Test]
    public void HistoryBonus_NoOutcomes_IsZero()
        => Assert.That(SignificanceScorer.HistoryBonus(0, 0, 0), Is.EqualTo(0));

    [Test]
    public void HistoryBonus_LopsidedOutcomes_ScoresHigherThanCoinflip()
    {
        var lopsided = SignificanceScorer.HistoryBonus(10, 9, 1);
        var coinflip  = SignificanceScorer.HistoryBonus(10, 5, 5);

        Assert.That(lopsided, Is.GreaterThan(coinflip));
        Assert.That(coinflip, Is.EqualTo(0)); // perfectly even split has zero directional signal
    }

    [Test]
    public void HistoryBonus_IsCappedAt30()
        => Assert.That(SignificanceScorer.HistoryBonus(100, 100, 0), Is.EqualTo(30));

    [Test]
    public void SourceBonus_NullConfidence_IsNeutralZero()
        => Assert.That(SignificanceScorer.SourceBonus(null), Is.EqualTo(0));

    [Test]
    public void SourceBonus_AboveAndBelowFiftyPercent_AreSymmetricAroundZero()
    {
        Assert.That(SignificanceScorer.SourceBonus(100), Is.EqualTo(10));
        Assert.That(SignificanceScorer.SourceBonus(0), Is.EqualTo(-10));
        Assert.That(SignificanceScorer.SourceBonus(50), Is.EqualTo(0));
    }

    [Test]
    public void WatchlistBonus_MatchAddsFlatEightPoints()
    {
        Assert.That(SignificanceScorer.WatchlistBonus(true), Is.EqualTo(8.0));
        Assert.That(SignificanceScorer.WatchlistBonus(false), Is.EqualTo(0.0));
    }

    [Test]
    public void RecencyMultiplier_FreshClaimIsFullWeight()
        => Assert.That(SignificanceScorer.RecencyMultiplier(0), Is.EqualTo(1.0));

    [Test]
    public void RecencyMultiplier_NeverDropsBelowHalf()
        => Assert.That(SignificanceScorer.RecencyMultiplier(365), Is.EqualTo(0.5));

    [Test]
    public void Combine_IsClampedToZeroToOneHundred()
    {
        var maxedOut = SignificanceScorer.Combine(100, 1.0, 30, 10, 8, 1.0);
        var negative = SignificanceScorer.Combine(20, 0.4, 0, -10, 0, 0.5);

        Assert.That(maxedOut, Is.EqualTo(100)); // 148 * 1.0 clamps down to 100
        Assert.That(negative, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Combine_HighMagnitudeRealizedCatalystWithStrongHistory_ScoresNearTop()
    {
        var score = SignificanceScorer.Combine(
            magnitudeScore: SignificanceScorer.MagnitudeScore("High"),
            confidenceMultiplier: SignificanceScorer.ConfidenceMultiplier(true, null),
            historyBonus: SignificanceScorer.HistoryBonus(10, 9, 1),
            sourceBonus: SignificanceScorer.SourceBonus(80),
            watchlistBonus: SignificanceScorer.WatchlistBonus(true),
            recencyMultiplier: SignificanceScorer.RecencyMultiplier(0));

        Assert.That(score, Is.GreaterThan(80));
    }

    [Test]
    public void Combine_LowMagnitudeOldPortentWithMixedHistory_ScoresNearBottom()
    {
        var score = SignificanceScorer.Combine(
            magnitudeScore: SignificanceScorer.MagnitudeScore("Low"),
            confidenceMultiplier: SignificanceScorer.ConfidenceMultiplier(false, "Low"),
            historyBonus: SignificanceScorer.HistoryBonus(10, 5, 5),
            sourceBonus: SignificanceScorer.SourceBonus(50),
            watchlistBonus: SignificanceScorer.WatchlistBonus(false),
            recencyMultiplier: SignificanceScorer.RecencyMultiplier(45));

        Assert.That(score, Is.LessThan(20));
    }

    // ---- ParseAffectedTickers / IsWatchlistMatch (internal, same assembly not required — test project references IdiotProof.Blazor as a ProjectReference so internals are visible only with InternalsVisibleTo; these are exercised indirectly via reflection-free public surface where possible) ----

    [Test]
    public void ParseAffectedTickers_ValidJsonArray_ParsesTickers()
    {
        var result = SignificanceScorer.ParseAffectedTickers("[\"AAPL\",\"MSFT\"]");
        Assert.That(result, Is.EquivalentTo(new[] { "AAPL", "MSFT" }));
    }

    [Test]
    public void ParseAffectedTickers_NullOrMalformed_ReturnsEmpty()
    {
        Assert.That(SignificanceScorer.ParseAffectedTickers(null), Is.Empty);
        Assert.That(SignificanceScorer.ParseAffectedTickers("not json"), Is.Empty);
        Assert.That(SignificanceScorer.ParseAffectedTickers("~340 issuers, not enumerable"), Is.Empty);
    }

    [Test]
    public void IsWatchlistMatch_SingleNameClaim_MatchesOwnTickerCaseInsensitively()
    {
        var claim = new ResearchClaim { Ticker = "aapl", IsMacro = false };
        Assert.That(SignificanceScorer.IsWatchlistMatch(claim, ["AAPL", "MSFT"]), Is.True);
        Assert.That(SignificanceScorer.IsWatchlistMatch(claim, ["MSFT"]), Is.False);
    }

    [Test]
    public void IsWatchlistMatch_MacroClaim_MatchesAnyAffectedTicker()
    {
        var claim = new ResearchClaim { Ticker = "", IsMacro = true, AffectedTickersJson = "[\"AAPL\",\"MSFT\"]" };
        Assert.That(SignificanceScorer.IsWatchlistMatch(claim, ["MSFT"]), Is.True);
        Assert.That(SignificanceScorer.IsWatchlistMatch(claim, ["GOOG"]), Is.False);
    }

    [Test]
    public void IsWatchlistMatch_EmptyWatchlist_NeverMatches()
    {
        var claim = new ResearchClaim { Ticker = "AAPL", IsMacro = false };
        Assert.That(SignificanceScorer.IsWatchlistMatch(claim, []), Is.False);
    }
}
