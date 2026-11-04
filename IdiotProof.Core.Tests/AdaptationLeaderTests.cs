using IdiotProof.Core.Adaptation;
using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Leaders;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Core.Tests;

/// <summary>
/// Adaptation shadow replays read the same prior-session leaders the Monitor uses (IP-T398), so a
/// leader-gated strategy's variants can actually trade instead of all scoring zero.
/// </summary>
public sealed class AdaptationLeaderTests
{
    private static readonly DateTime Open = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    private static Candle Bar(string symbol, int i, double open, double close) => new()
    {
        Symbol = symbol, StartUtc = Open.AddMinutes(i), EndUtc = Open.AddMinutes(i + 1),
        Open = (decimal)open, Close = (decimal)close, High = (decimal)Math.Max(open, close), Low = (decimal)Math.Min(open, close), Volume = 20_000,
    };

    /// <summary>Flat at 3.00 for 10 bars, then up 0.3% a bar, so a 1% take-profit resolves well before the data ends.</summary>
    private static List<Candle> Follower(int n)
    {
        var list = new List<Candle>(n);
        double p = 3.0;
        for (int i = 0; i < n; i++)
        {
            double o = p;
            if (i >= 10) p *= 1.003;
            list.Add(Bar("FOLL", i, o, p));
        }
        return list;
    }

    private static List<Candle> Leader(int n, double stepPct)
    {
        var list = new List<Candle>(n);
        double p = 50;
        for (int i = 0; i < n; i++) { double o = p; p *= 1 + stepPct; list.Add(Bar("LEAD", i, o, p)); }
        return list;
    }

    private static BacktestLeaderContext Context(List<Candle> leaderBars) =>
        new([new LeaderLink("LEAD", 1, 0.6)], new Dictionary<string, IReadOnlyList<Candle>> { ["LEAD"] = leaderBars });

    private static StrategyDefinition LeaderGated() =>
        Stock.Ticker("FOLL").LeaderConfirms(1, 0.2).Long().TakeProfit(1.0).StopLoss(2.0).Build();

    [Test]
    public void ShadowTrades_LeaderGatedVariant_TradesWithLeaderContext_AndNotWithout()
    {
        var bars = Follower(60);
        var withRisingLeader = AdaptationService.ShadowTrades(LeaderGated(), bars, Context(Leader(60, 0.005))).ToList();
        var withFallingLeader = AdaptationService.ShadowTrades(LeaderGated(), bars, Context(Leader(60, -0.005))).ToList();
        var withoutContext = AdaptationService.ShadowTrades(LeaderGated(), bars).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(withRisingLeader, Is.Not.Empty, "the leader climbed first, so the shadow replay can enter");
            Assert.That(withFallingLeader, Is.Empty);
            Assert.That(withoutContext, Is.Empty, "no leader context stays fail-closed, as before");
        });
    }

    [Test]
    public void ShadowTrades_NeverReadALeaderBarAfterTheReplayedBars()
    {
        // The leader is flat for the 40 replayed bars and only climbs afterwards; the shadow replay must not see it.
        var leader = new List<Candle>();
        double p = 50;
        for (int i = 0; i < 80; i++) { double o = p; if (i >= 40) p *= 1.01; leader.Add(Bar("LEAD", i, o, p)); }
        var trades = AdaptationService.ShadowTrades(LeaderGated(), Follower(40), Context(leader)).ToList();
        Assert.That(trades, Is.Empty);
    }

    [Test]
    public async Task AdaptAsync_AsksForLeadersOnlyWhenTheStrategyUsesALeaderGuard()
    {
        var factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"adapt-leader-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();
        var service = new AdaptationService(factory, new SettingsRepository(factory), new RiskGuardianService(factory));

        var gated = await SeedAsync(factory, Stock.Ticker("FOLL").LeaderConfirms(1, 0.2).Long().Quantity(1).StopLossPercent(5).Repeat().Build());
        var plain = await SeedAsync(factory, Stock.Ticker("FOLL").IsAboveVwap().Long().Quantity(1).StopLossPercent(5).Repeat().Build());
        await service.SetOptInAsync(gated, true);
        await service.SetOptInAsync(plain, true);

        var asked = new List<string>();
        Task<BacktestLeaderContext?> Provider(StrategyDefinition def, CancellationToken _)
        {
            asked.Add(def.Symbol);
            return Task.FromResult<BacktestLeaderContext?>(Context(Leader(120, 0.005)));
        }

        var now = Open.AddMinutes(90);
        await service.AdaptAsync(plain, Follower(120), Open, now, leaders: Provider);
        Assert.That(asked, Is.Empty, "a strategy without leader guards fetches no leaders");

        var result = await service.AdaptAsync(gated, Follower(120), Open, now, leaders: Provider);
        Assert.That(asked, Is.EqualTo(new[] { "FOLL" }));
        Assert.That(result.Outcome, Is.Not.EqualTo(AdaptationService.Refused), result.Reason);
    }

    private static async Task<Guid> SeedAsync(IDbContextFactory<AppDbContext> factory, StrategyDefinition def)
    {
        var s = new Strategy
        {
            OwnerUserId = Guid.NewGuid(), Symbol = def.Symbol, Title = "leader adapt",
            ScriptJson = StrategyJson.Serialize(def), ScriptText = def.ToScript(), IsActive = true, BrokerMode = "Paper",
        };
        await using var db = await factory.CreateDbContextAsync();
        db.Strategies.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }
}
