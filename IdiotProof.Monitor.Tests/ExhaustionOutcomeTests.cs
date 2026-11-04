using IdiotProof.Core.Data;
using IdiotProof.Core.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Monitor.Tests;

/// <summary>IP-US-S4: an armed exhaustion decision gets its realized P&amp;L once its strategy's trades have closed.</summary>
public sealed class ExhaustionOutcomeTests
{
    private static readonly DateTime Decided = new(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc);
    private IDbContextFactory<AppDbContext> db = null!;

    [SetUp]
    public void SetUp() => db = new ServiceCollection()
        .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"exhaust-outcome-{Guid.NewGuid():N}"))
        .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();

    // RecordOutcomesAsync only reads and writes SQL, so the feed/broker/settings collaborators are not needed.
    private ExhaustionScanner Scanner() => new(null!, null!, null!, null!, null!, db, NullLogger<ExhaustionScanner>.Instance);

    private async Task<ExhaustionDecisionRow> DecisionAsync(bool armed, Guid? strategyId)
    {
        var row = new ExhaustionDecisionRow
        {
            OwnerUserId = Guid.NewGuid(), Symbol = "SNDL", EtDate = "2026-11-02", DecidedUtc = Decided, Side = "Long",
            Armed = armed, StrategyId = strategyId, ReversalProbability = 0.62, FeaturesJson = "{}",
        };
        await using var c = await db.CreateDbContextAsync();
        c.ExhaustionDecisions.Add(row);
        await c.SaveChangesAsync();
        return row;
    }

    private async Task TradeAsync(Guid strategyId, DateTime entryUtc, decimal? pnl, decimal? ret)
    {
        await using var c = await db.CreateDbContextAsync();
        c.TradeDiary.Add(new TradeDiaryEntry
        {
            StrategyId = strategyId, Symbol = "SNDL", Direction = "Long", EntryUtc = entryUtc, EntryPrice = 1.00m, Quantity = 1,
            ExitUtc = pnl is null ? null : entryUtc.AddMinutes(20), ExitPrice = pnl is null ? null : 1.00m + pnl,
            RealizedPnL = pnl, ReturnPercent = ret,
            Status = pnl is null ? TradeDiaryStatus.Open : TradeDiaryStatus.Closed,
        });
        await c.SaveChangesAsync();
    }

    private async Task<ExhaustionDecisionRow> ReloadAsync(Guid id)
    {
        await using var c = await db.CreateDbContextAsync();
        return await c.ExhaustionDecisions.AsNoTracking().SingleAsync(d => d.Id == id);
    }

    [Test]
    public async Task ArmedDecision_GetsRealizedPnL_OnceItsTradeCloses()
    {
        var strategy = Guid.NewGuid();
        var d = await DecisionAsync(armed: true, strategy);
        await TradeAsync(strategy, Decided.AddMinutes(3), 0.05m, 5m);

        Assert.That(await Scanner().RecordOutcomesAsync(), Is.EqualTo(1));
        var row = await ReloadAsync(d.Id);
        Assert.That(row.RealizedTrades, Is.EqualTo(1));
        Assert.That(row.RealizedPnL, Is.EqualTo(0.05m));
        Assert.That(row.RealizedReturnPercent, Is.EqualTo(5m));
        Assert.That(row.OutcomeRecordedUtc, Is.Not.Null);
        Assert.That(await Scanner().RecordOutcomesAsync(), Is.EqualTo(0), "a recorded outcome is never rewritten");
    }

    [Test]
    public async Task OpenTrade_WaitsForTheExit_BeforeLabeling()
    {
        var strategy = Guid.NewGuid();
        var d = await DecisionAsync(armed: true, strategy);
        await TradeAsync(strategy, Decided.AddMinutes(3), pnl: null, ret: null);

        Assert.That(await Scanner().RecordOutcomesAsync(), Is.EqualTo(0));
        Assert.That((await ReloadAsync(d.Id)).OutcomeRecordedUtc, Is.Null);
    }

    [Test]
    public async Task SkippedDecisions_AndTradesBeforeTheDecision_AreIgnored()
    {
        var strategy = Guid.NewGuid();
        var skipped = await DecisionAsync(armed: false, strategyId: null);
        var armed = await DecisionAsync(armed: true, strategy);
        await TradeAsync(strategy, Decided.AddMinutes(-30), -0.10m, -10m);

        Assert.That(await Scanner().RecordOutcomesAsync(), Is.EqualTo(0));
        Assert.That((await ReloadAsync(skipped.Id)).OutcomeRecordedUtc, Is.Null);
        Assert.That((await ReloadAsync(armed.Id)).OutcomeRecordedUtc, Is.Null, "a trade entered before the decision is not its outcome");
    }
}
