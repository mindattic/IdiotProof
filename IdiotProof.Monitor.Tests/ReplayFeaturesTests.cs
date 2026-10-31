using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Monitor;

namespace IdiotProof.Monitor.Tests;

/// <summary>
/// IP-T9: <see cref="ReplayFeatures.Extract"/>, the single source of the ML feature store and the replay CSV
/// export. Builds <see cref="ReplayRun.DataJson"/> fixtures and checks the flattened
/// <see cref="ReplayBar"/> and <see cref="ReplayTrade"/> rows.
/// </summary>
[TestFixture]
public sealed class ReplayFeaturesTests
{
    private static ReplayRun Run(object data) => new()
    {
        Symbol = "GAPR", Strategy = "gapper", DateEt = "2026-10-30", Feed = "sip",
        GeneratedUtc = new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc),
        DataJson = JsonSerializer.Serialize(data),
    };

    private static ReplayRun RunJson(string json) => new() { Symbol = "X", DataJson = json };

    private static readonly object Fixture = new
    {
        conditions = new[] { "IsGapBetween(15, 60)", "IsAboveVwap()", "IsPriceBetween(1, 20)" },
        bars = new object[]
        {
            new { et = "04:00", o = 2.0, h = 2.2, l = 1.9, c = 2.1, v = 12000, vwap = 2.05, whigh = 2.2, volx = 1.5, inSession = true, cnd = new object[] { true, false, true }, fire = false, exit = false },
            new { et = "04:01", o = 2.1, h = 2.5, l = 2.1, c = 2.4, v = 30000, vwap = 2.2, whigh = 2.5, volx = 3.25, inSession = true, cnd = new object[] { true, true, true }, fire = true, exit = false },
            new { et = "09:31", o = 2.6, h = 2.7, l = 2.5, c = 2.55, v = 5000, vwap = 0.0, whigh = 0.0, volx = 0.8, inSession = false, cnd = new object?[] { "true", 1, null, true }, fire = false, exit = true },
        },
        payoffs = new object[]
        {
            new { entryEt = "04:01", exitEt = "09:31", entryPx = 2.4, exitPx = 2.55, pnlPct = 6.25, reason = "TakeProfit" },
            new { entryEt = "09:31", exitEt = "09:45", entryPx = 2.6, exitPx = 2.5, pnlPct = -3.85, reason = "StopLoss" },
            new { entryEt = "10:15", exitEt = "10:20", entryPx = 3.0, exitPx = 3.0, pnlPct = 0.0, reason = "SellBy" },
        },
    };

    [Test]
    public void Bars_CarryOhlcvIndicatorsFlagsAndConditionCounts()
    {
        var run = Run(Fixture);
        var (_, bars) = ReplayFeatures.Extract(run);

        Assert.That(bars, Has.Count.EqualTo(3));
        var b0 = bars[0];
        var b1 = bars[1];
        var b2 = bars[2];
        Assert.Multiple(() =>
        {
            Assert.That(b0.ReplayRunId, Is.EqualTo(run.Id));
            Assert.That(b0.Symbol, Is.EqualTo("GAPR"));
            Assert.That(b0.DateEt, Is.EqualTo("2026-10-30"));
            Assert.That(b0.Strategy, Is.EqualTo("gapper"));
            Assert.That(b0.Et, Is.EqualTo("04:00"));
            Assert.That(b0.Min, Is.EqualTo(240));
            Assert.That((b0.Open, b0.High, b0.Low, b0.Close), Is.EqualTo((2.0, 2.2, 1.9, 2.1)));
            Assert.That(b0.Volume, Is.EqualTo(12000L));
            Assert.That(b0.Vwap, Is.EqualTo(2.05));
            Assert.That(b0.WindowHigh, Is.EqualTo(2.2));
            Assert.That(b0.Volx, Is.EqualTo(1.5));
            Assert.That(b0.InSession, Is.True);
            Assert.That(b0.CondPassed, Is.EqualTo(2));
            Assert.That(b0.CondTotal, Is.EqualTo(3));
            Assert.That(b0.Fire, Is.False);
            Assert.That(b0.Exit, Is.False);

            Assert.That(b1.Min, Is.EqualTo(241));
            Assert.That(b1.CondPassed, Is.EqualTo(3));
            Assert.That(b1.Fire, Is.True);

            Assert.That(b2.Min, Is.EqualTo(571));
            Assert.That(b2.CondPassed, Is.EqualTo(1), "only JSON true counts, not \"true\", 1 or null");
            Assert.That(b2.CondTotal, Is.EqualTo(3), "total comes from the conditions array, not the cnd length");
            Assert.That(b2.InSession, Is.False);
            Assert.That(b2.Exit, Is.True);
        });
    }

    [Test]
    public void Trades_JoinTheEntryBar_AndComputeDistances()
    {
        var run = Run(Fixture);
        var (trades, _) = ReplayFeatures.Extract(run);

        Assert.That(trades, Has.Count.EqualTo(3));
        var t = trades[0];
        Assert.Multiple(() =>
        {
            Assert.That(t.ReplayRunId, Is.EqualTo(run.Id));
            Assert.That(t.Symbol, Is.EqualTo("GAPR"));
            Assert.That(t.Feed, Is.EqualTo("sip"));
            Assert.That(t.GeneratedUtc, Is.EqualTo(run.GeneratedUtc));
            Assert.That(t.EntryEt, Is.EqualTo("04:01"));
            Assert.That(t.EntryMin, Is.EqualTo(241));
            Assert.That(t.ExitEt, Is.EqualTo("09:31"));
            Assert.That(t.ExitMin, Is.EqualTo(571));
            Assert.That(t.HoldMin, Is.EqualTo(330));
            Assert.That(t.EntryPx, Is.EqualTo(2.4));
            Assert.That(t.ExitPx, Is.EqualTo(2.55));
            Assert.That(t.PnlPct, Is.EqualTo(6.25));
            Assert.That(t.Reason, Is.EqualTo("TakeProfit"));
            Assert.That(t.Won, Is.True);
            Assert.That(t.EntryVwap, Is.EqualTo(2.2), "joined from the 04:01 bar");
            Assert.That(t.EntryWindowHigh, Is.EqualTo(2.5));
            Assert.That(t.EntryVolx, Is.EqualTo(3.25));
            Assert.That(t.DistVwapPct, Is.EqualTo((2.4 - 2.2) / 2.2 * 100).Within(1e-9));
            Assert.That(t.DistWinHighPct, Is.EqualTo((2.4 - 2.5) / 2.5 * 100).Within(1e-9));
        });
    }

    [Test]
    public void Trades_ZeroVwapOrWindowHigh_GiveZeroDistance_LossIsNotAWin()
    {
        var t = ReplayFeatures.Extract(Run(Fixture)).Trades[1];
        Assert.Multiple(() =>
        {
            Assert.That(t.EntryVwap, Is.Zero);
            Assert.That(t.EntryWindowHigh, Is.Zero);
            Assert.That(t.EntryVolx, Is.EqualTo(0.8), "the bar itself was found");
            Assert.That(t.DistVwapPct, Is.Zero);
            Assert.That(t.DistWinHighPct, Is.Zero);
            Assert.That(t.Won, Is.False);
            Assert.That(t.HoldMin, Is.EqualTo(14));
        });
    }

    [Test]
    public void Trades_MissingEntryBar_ZeroJoinedFeatures_AndBreakevenIsNotAWin()
    {
        var t = ReplayFeatures.Extract(Run(Fixture)).Trades[2];
        Assert.Multiple(() =>
        {
            Assert.That(t.EntryVwap, Is.Zero);
            Assert.That(t.EntryWindowHigh, Is.Zero);
            Assert.That(t.EntryVolx, Is.Zero);
            Assert.That(t.DistVwapPct, Is.Zero);
            Assert.That(t.DistWinHighPct, Is.Zero);
            Assert.That(t.Won, Is.False, "pnl 0 is not a win");
        });
    }

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("{ \"bars\": [ ")]
    public void InvalidJson_ReturnsEmptyLists(string json)
    {
        var (trades, bars) = ReplayFeatures.Extract(RunJson(json));
        Assert.Multiple(() =>
        {
            Assert.That(trades, Is.Empty);
            Assert.That(bars, Is.Empty);
        });
    }

    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("{\"bars\":{},\"payoffs\":\"none\",\"conditions\":5}")]
    public void MissingOrNonArraySections_ReturnEmptyLists(string json)
    {
        var (trades, bars) = ReplayFeatures.Extract(RunJson(json));
        Assert.Multiple(() =>
        {
            Assert.That(trades, Is.Empty);
            Assert.That(bars, Is.Empty);
        });
    }

    [Test]
    public void NonObjectEntries_AreSkipped()
    {
        // IP-T341: a stray non-object entry used to throw out of Extract and abort replay-export's backfill.
        var (trades, bars) = ReplayFeatures.Extract(RunJson("""
            {"bars":[1,"x",null,{"et":"04:00","c":2}],"payoffs":[[],{"entryEt":"04:00","exitEt":"04:05","pnlPct":1}]}
            """));
        Assert.Multiple(() =>
        {
            Assert.That(bars.Single().Close, Is.EqualTo(2.0));
            Assert.That(trades.Single().HoldMin, Is.EqualTo(5));
            Assert.That(trades.Single().Won, Is.True);
        });
    }

    [Test]
    public void SparseBarsAndTrades_FallBackToZeroAndEmpty()
    {
        var (trades, bars) = ReplayFeatures.Extract(RunJson("""
            {"bars":[{"et":"bogus","o":"1.5","fire":"yes"}],
             "payoffs":[{"entryEt":"25","pnlPct":"5"}]}
            """));
        Assert.Multiple(() =>
        {
            Assert.That(bars.Single().Min, Is.Zero, "unparseable time");
            Assert.That(bars.Single().Open, Is.Zero, "non-numeric price");
            Assert.That(bars.Single().Fire, Is.False, "non-boolean flag");
            Assert.That(bars.Single().CondPassed, Is.Zero);
            Assert.That(bars.Single().CondTotal, Is.Zero, "no conditions array");
            Assert.That(trades.Single().EntryMin, Is.Zero);
            Assert.That(trades.Single().ExitEt, Is.EqualTo(""));
            Assert.That(trades.Single().PnlPct, Is.Zero);
            Assert.That(trades.Single().Won, Is.False);
        });
    }
}
