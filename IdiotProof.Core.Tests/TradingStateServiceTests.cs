using IdiotProof.Blazor.Services;
using IdiotProof.Models;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T13: <see cref="TradingStateService"/>, the singleton backing the live dashboard: signal cap and
/// ordering, today-only counting, per-user position isolation, snapshot semantics, change events and
/// concurrent writers.
/// </summary>
[TestFixture]
public sealed class TradingStateServiceTests
{
    private static TradeSignal Signal(string symbol = "AAPL", string user = "u1", DateTime? at = null, string reason = "") =>
        new() { Symbol = symbol, UserId = user, GeneratedUtc = at ?? DateTime.UtcNow, Reason = reason };

    private static Position Pos(string symbol, decimal pnl) => new() { Symbol = symbol, Quantity = 1, UnrealizedPnl = pnl };

    [Test]
    public void AddSignal_KeepsNewestFirst_AndCapsAt500()
    {
        var svc = new TradingStateService();
        for (var i = 0; i < 510; i++)
            svc.AddSignal(Signal(reason: i.ToString()));

        var recent = svc.RecentSignals;
        Assert.Multiple(() =>
        {
            Assert.That(recent, Has.Count.EqualTo(500));
            Assert.That(recent[0].Reason, Is.EqualTo("509"), "newest first");
            Assert.That(recent[^1].Reason, Is.EqualTo("10"), "oldest ten dropped");
        });
    }

    [Test]
    public void TotalSignalsToday_CountsOnlyTodayUtc()
    {
        var svc = new TradingStateService();
        svc.AddSignal(Signal());
        svc.AddSignal(Signal(at: DateTime.UtcNow.AddDays(-1)));
        svc.AddSignal(Signal(at: DateTime.UtcNow.AddDays(-30)));
        svc.AddSignal(Signal());

        Assert.Multiple(() =>
        {
            Assert.That(svc.TotalSignalsToday, Is.EqualTo(2));
            Assert.That(svc.RecentSignals, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public void GetPositionsForUser_ReturnsOnlyThatUser_KeyedByBroker()
    {
        var svc = new TradingStateService();
        svc.UpdatePositions("user1", BrokerType.Alpaca, [Pos("AAPL", 10)]);
        svc.UpdatePositions("user1", BrokerType.Sandbox, [Pos("MSFT", 5)]);
        svc.UpdatePositions("user2", BrokerType.Alpaca, [Pos("TSLA", 99)]);

        var mine = svc.GetPositionsForUser("user1");
        Assert.Multiple(() =>
        {
            Assert.That(mine.Keys, Is.EquivalentTo(new[] { BrokerType.Alpaca.ToString(), BrokerType.Sandbox.ToString() }));
            Assert.That(mine[BrokerType.Alpaca.ToString()].Single().Symbol, Is.EqualTo("AAPL"));
            Assert.That(mine[BrokerType.Sandbox.ToString()].Single().Symbol, Is.EqualTo("MSFT"));
            Assert.That(svc.GetPositionsForUser("nobody"), Is.Empty);
        });
    }

    [Test]
    public void GetPositionsForUser_DoesNotLeakBetweenPrefixLikeIds()
    {
        var svc = new TradingStateService();
        svc.UpdatePositions("user1", BrokerType.Alpaca, [Pos("AAPL", 10)]);
        svc.UpdatePositions("user10", BrokerType.Alpaca, [Pos("TSLA", 99)]);

        Assert.Multiple(() =>
        {
            Assert.That(svc.GetPositionsForUser("user1")[BrokerType.Alpaca.ToString()].Single().Symbol, Is.EqualTo("AAPL"));
            Assert.That(svc.GetPositionsForUser("user10")[BrokerType.Alpaca.ToString()].Single().Symbol, Is.EqualTo("TSLA"));
            Assert.That(svc.GetTodaysPnlForUser("user1"), Is.EqualTo(10m));
        });
    }

    [Test]
    public void UpdatePositions_ReplacesTheUserBrokerList()
    {
        var svc = new TradingStateService();
        svc.UpdatePositions("u", BrokerType.Alpaca, [Pos("AAPL", 1), Pos("MSFT", 2)]);
        svc.UpdatePositions("u", BrokerType.Alpaca, [Pos("NVDA", 3)]);
        Assert.That(svc.GetPositionsForUser("u")[BrokerType.Alpaca.ToString()].Select(p => p.Symbol), Is.EqualTo(new[] { "NVDA" }));
    }

    [Test]
    public void GetTodaysPnlForUser_SumsUnrealizedAcrossBrokers()
    {
        var svc = new TradingStateService();
        svc.UpdatePositions("u", BrokerType.Alpaca, [Pos("AAPL", 10.5m), Pos("MSFT", -3m)]);
        svc.UpdatePositions("u", BrokerType.Sandbox, [Pos("TSLA", 2.25m)]);
        svc.UpdatePositions("other", BrokerType.Alpaca, [Pos("AMD", 1000m)]);

        Assert.Multiple(() =>
        {
            Assert.That(svc.GetTodaysPnlForUser("u"), Is.EqualTo(9.75m));
            Assert.That(svc.GetTodaysPnlForUser("nobody"), Is.Zero);
        });
    }

    [Test]
    public void GetSignalsFor_FiltersSymbolCaseInsensitively_WithAndWithoutUser()
    {
        var svc = new TradingStateService();
        svc.AddSignal(Signal("AAPL", "u1"));
        svc.AddSignal(Signal("aapl", "u2"));
        svc.AddSignal(Signal("MSFT", "u1"));

        Assert.Multiple(() =>
        {
            Assert.That(svc.GetSignalsFor("Aapl"), Has.Count.EqualTo(2));
            Assert.That(svc.GetSignalsFor("AAPL", "u1").Single().UserId, Is.EqualTo("u1"));
            Assert.That(svc.GetSignalsFor("AAPL", "u3"), Is.Empty);
            Assert.That(svc.GetRecentSignalsForUser("u1"), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void LivePrices_IsASnapshot_CaseInsensitive()
    {
        var svc = new TradingStateService();
        svc.UpdatePrice(new LatestPrice("AAPL", 100m, DateTime.UtcNow, "test"));
        var snap = (IDictionary<string, LatestPrice>)svc.LivePrices;
        snap["MSFT"] = new LatestPrice("MSFT", 1m, DateTime.UtcNow, "test");
        snap.Remove("AAPL");

        Assert.Multiple(() =>
        {
            Assert.That(svc.LivePrices.ContainsKey("aapl"), Is.True);
            Assert.That(svc.LivePrices.ContainsKey("MSFT"), Is.False);
            Assert.That(svc.LivePrices["AAPL"].Price, Is.EqualTo(100m));
        });

        svc.UpdatePrice(new LatestPrice("aapl", 101m, DateTime.UtcNow, "test"));
        Assert.That(svc.LivePrices, Has.Count.EqualTo(1), "same symbol, different case, overwrites");
    }

    [Test]
    public void SignalVotes_AndRecentSignals_AreSnapshots()
    {
        var svc = new TradingStateService();
        svc.StoreVote("k1", new LlmVotingResult());
        var votes = (IDictionary<string, LlmVotingResult>)svc.SignalVotes;
        votes["k2"] = new LlmVotingResult();
        votes.Remove("k1");

        svc.AddSignal(Signal());
        var signals = (IList<TradeSignal>)svc.RecentSignals;
        signals.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(svc.SignalVotes.Keys, Is.EqualTo(new[] { "k1" }));
            Assert.That(svc.RecentSignals, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void OnStateChanged_FiresOnEveryMutation()
    {
        var svc = new TradingStateService();
        var fired = 0;
        svc.OnStateChanged += () => fired++;

        svc.AddSignal(Signal());
        svc.UpdatePrice(new LatestPrice("AAPL", 1m, DateTime.UtcNow, "t"));
        svc.UpdatePositions("u", BrokerType.Alpaca, []);
        svc.StoreVote("k", new LlmVotingResult());
        svc.SetEngineRunning(true);
        svc.RecordEvaluation();

        Assert.Multiple(() =>
        {
            Assert.That(fired, Is.EqualTo(6));
            Assert.That(svc.IsEngineRunning, Is.True);
            Assert.That(svc.LastEvaluationUtc, Is.Not.Null);
        });
    }

    [Test]
    public void ConcurrentAddSignalAndUpdatePrice_DoNotThrow()
    {
        var svc = new TradingStateService();
        var changes = 0;
        svc.OnStateChanged += () => Interlocked.Increment(ref changes);

        Assert.DoesNotThrow(() => Parallel.For(0, 2000, i =>
        {
            if (i % 2 == 0) svc.AddSignal(Signal($"S{i % 17}"));
            else svc.UpdatePrice(new LatestPrice($"S{i % 17}", i, DateTime.UtcNow, "t"));
            _ = svc.RecentSignals.Count;
            _ = svc.LivePrices.Count;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(svc.RecentSignals, Has.Count.EqualTo(500));
            Assert.That(svc.TotalSignalsToday, Is.EqualTo(1000));
            Assert.That(changes, Is.EqualTo(2000));
        });
    }
}
