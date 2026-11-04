using IdiotProof.Core.Adaptation;
using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Adaptation;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Core.Tests;

public sealed class AdaptationServiceTests
{
    private static readonly DateTime Open = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);
    private IDbContextFactory<AppDbContext> factory = null!;
    private AdaptationService service = null!;

    [SetUp]
    public void SetUp()
    {
        factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"adapt-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();
        service = new AdaptationService(factory, new SettingsRepository(factory), new RiskGuardianService(factory));
    }

    [Test]
    public async Task LiveStrategy_IsRefused_EvenWhenOptedIn_AndNothingIsWritten()
    {
        var id = await SeedAsync("Live");
        await service.SetOptInAsync(id, true);
        var result = await service.AdaptAsync(id, Tape(120), Open, Open.AddMinutes(120));
        Assert.That(result.Outcome, Is.EqualTo(AdaptationService.Refused));
        Assert.That(result.Reason, Does.Contain("paper-only"));
        await AssertUntouchedAsync(id);
    }

    [Test]
    public async Task NotOptedIn_IsRefused()
    {
        var id = await SeedAsync("Paper");
        var result = await service.AdaptAsync(id, Tape(120), Open, Open.AddMinutes(120));
        Assert.That(result.Reason, Does.Contain("opted"));
        await AssertUntouchedAsync(id);
    }

    [Test]
    public async Task HoldingAPosition_IsRefused()
    {
        var id = await SeedAsync("Paper", positionQty: 10);
        await service.SetOptInAsync(id, true);
        var result = await service.AdaptAsync(id, Tape(120), Open, Open.AddMinutes(120));
        Assert.That(result.Reason, Does.Contain("holding"));
    }

    [Test]
    public async Task FirstThinMinutes_AreRefusedByTheInformationGate()
    {
        var id = await SeedAsync("Paper");
        await service.SetOptInAsync(id, true);
        var result = await service.AdaptAsync(id, Tape(120), Open, Open.AddMinutes(4));
        Assert.That(result.Outcome, Is.EqualTo(AdaptationService.Refused));
        Assert.That(result.Reason, Does.Contain("waiting for information"));
        await AssertUntouchedAsync(id);
    }

    [Test]
    public async Task OptedInPaperStrategy_IsRetunedThroughTheRiskGuardian_AndTheDecisionIsPersisted()
    {
        var id = await SeedAsync("Paper");
        await service.SetOptInAsync(id, true);
        string original;
        await using (var db = await factory.CreateDbContextAsync()) original = (await db.Strategies.FindAsync(id))!.ScriptJson!;

        var now = Open.AddMinutes(90);
        var result = await service.AdaptAsync(id, Tape(120), Open, now);

        Assert.That(result.Outcome, Is.EqualTo(AdaptationService.Applied), result.Reason);
        await using var check = await factory.CreateDbContextAsync();
        var decision = await check.AdaptationDecisions.SingleAsync();
        Assert.That(decision.StrategyId, Is.EqualTo(id));
        Assert.That(decision.OriginalScriptJson, Is.EqualTo(original), "the replaced script is kept so it can be reverted");
        Assert.That(decision.ClosedBars, Is.EqualTo(90), "only bars closed by 'now' are read");
        Assert.That(decision.VariantsJson, Does.Contain("ProbabilityBest"));
        Assert.That(decision.RiskSummary, Does.Contain("APPROVED"));

        var adapted = StrategyJson.Deserialize((await check.Strategies.FindAsync(id))!.ScriptJson!);
        var chosen = decision.ChosenVariant;
        Assert.That(adapted.StopLossPercent, Is.Not.EqualTo(5.0), $"chose {chosen}");
        Assert.That(adapted.StopLossPercent, Is.LessThan(5.0));
    }

    [Test]
    public void ShadowTrades_DropTradesTheBacktesterOnlyClosedBecauseTheDataEnded()
    {
        var def = Stock.Ticker("PNY").IsAboveVwap().Long().StopLossPercent(50).Build();
        var trades = AdaptationService.ShadowTrades(def, Tape(60).Where(c => c.EndUtc <= Open.AddMinutes(60)).ToList()).ToList();
        Assert.That(trades, Is.Empty, "a 50% stop never trips, so the only trade is the end-of-session close");
    }

    [Test]
    public async Task RecordOutcomes_JoinsClosedDiaryTradesAfterTheDecision()
    {
        var id = await SeedAsync("Paper");
        await service.SetOptInAsync(id, true);
        var now = Open.AddMinutes(90);
        await service.AdaptAsync(id, Tape(120), Open, now);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.TradeDiary.Add(new TradeDiaryEntry { StrategyId = id, Symbol = "PNY", EntryUtc = now.AddMinutes(5), EntryPrice = 3, Quantity = 1,
                ExitUtc = now.AddMinutes(20), ExitPrice = 3.1m, RealizedPnL = 0.1m, ReturnPercent = 3.33m });
            await db.SaveChangesAsync();
        }
        Assert.That(await service.RecordOutcomesAsync(), Is.EqualTo(1));
        await using var check = await factory.CreateDbContextAsync();
        var d = await check.AdaptationDecisions.SingleAsync();
        Assert.That(d.RealizedTrades, Is.EqualTo(1));
        Assert.That(d.RealizedPnL, Is.EqualTo(0.1m));
    }

    [Test]
    public async Task GlobalSwitch_IsOffByDefault()
    {
        Assert.That(await service.IsGloballyEnabledAsync(), Is.False);
    }

    private async Task<Guid> SeedAsync(string mode, int positionQty = 0)
    {
        var def = Stock.Ticker("PNY").IsAboveVwap().Long().Quantity(1).StopLossPercent(5).Repeat().Build();
        var s = new Strategy
        {
            OwnerUserId = Guid.NewGuid(),
            Symbol = "PNY",
            Title = "adapt me",
            ScriptJson = StrategyJson.Serialize(def),
            ScriptText = def.ToScript(),
            IsActive = true,
            BrokerMode = mode,
            PositionQty = positionQty,
        };
        await using var db = await factory.CreateDbContextAsync();
        db.Strategies.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    private async Task AssertUntouchedAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        Assert.That(await db.AdaptationDecisions.CountAsync(), Is.EqualTo(0));
    }

    /// <summary>A penny stock wandering with ~0.6% one-minute noise.</summary>
    private static List<Candle> Tape(int bars)
    {
        var rng = new Random(17);
        var list = new List<Candle>();
        double p = 3.0;
        for (int i = 0; i < bars; i++)
        {
            double o = p;
            p *= 1 + 0.006 * Gaussian(rng);
            double hi = Math.Max(o, p) * (1 + 0.002 * rng.NextDouble());
            double lo = Math.Min(o, p) * (1 - 0.002 * rng.NextDouble());
            list.Add(new Candle
            {
                Symbol = "PNY",
                StartUtc = Open.AddMinutes(i),
                EndUtc = Open.AddMinutes(i + 1),
                Open = (decimal)o, Close = (decimal)p, High = (decimal)hi, Low = (decimal)lo,
                Volume = 20_000,
            });
        }
        return list;
    }

    private static double Gaussian(Random rng) =>
        Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
}
