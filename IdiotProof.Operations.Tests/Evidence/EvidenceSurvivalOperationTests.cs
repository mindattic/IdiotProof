using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Operations.Evidence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Evidence;

public class EvidenceSurvivalOperationTests
{
    private ServiceProvider services = null!;
    private IDbContextFactory<AppDbContext> db = null!;

    [SetUp]
    public void SetUp()
    {
        services = new ServiceCollection()
            .AddLogging()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"surv-{Guid.NewGuid():N}"))
            .BuildServiceProvider();
        db = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    [TearDown]
    public void TearDown() => services.Dispose();

    private OperationContext Ctx() => new() { Services = services, Caller = OperationCaller.Cli, Clock = TimeProvider.System };

    private static ReplayTrade Trade(string strategy, int hold, string reason, double vwapDist = 0, double price = 2.5) => new()
    {
        ReplayRunId = Guid.NewGuid(), Symbol = "PNY", DateEt = "2026-10-01", Strategy = strategy, HoldMin = hold, Reason = reason,
        EntryPx = price, ExitPx = price, EntryVolx = 1.5, DistVwapPct = vwapDist, DistWinHighPct = -1, GeneratedUtc = DateTime.UtcNow,
    };

    [Test]
    public void ReasonsMapToCompetingCauses_AndCutoffsCensor()
    {
        Assert.That(EvidenceSurvivalOperation.CauseOf("TargetHit"), Is.EqualTo(1));
        Assert.That(EvidenceSurvivalOperation.CauseOf("StopLoss"), Is.EqualTo(2));
        Assert.That(EvidenceSurvivalOperation.CauseOf("TrailingStop"), Is.EqualTo(3));
        Assert.That(EvidenceSurvivalOperation.CauseOf("PeakGiveback"), Is.EqualTo(3));
        Assert.That(EvidenceSurvivalOperation.CauseOf("SellByTime"), Is.Zero);
        Assert.That(EvidenceSurvivalOperation.CauseOf("EndOfData"), Is.Zero);
        Assert.That(EvidenceSurvivalOperation.CauseOf("WhileFailed"), Is.Zero);
    }

    [Test]
    public async Task ReportsIncidenceAtCheckpoints_AndASellBy_PerStrategy()
    {
        await using (var c = await db.CreateDbContextAsync())
        {
            // 12 targets by minute 10, 6 stops at minute 20, 2 sell-by censors at 60: target incidence flattens at 10.
            for (int i = 0; i < 12; i++) c.ReplayTrades.Add(Trade("gapper", 5 + i % 6, "TargetHit"));
            for (int i = 0; i < 6; i++) c.ReplayTrades.Add(Trade("gapper", 20, "StopLoss"));
            for (int i = 0; i < 2; i++) c.ReplayTrades.Add(Trade("gapper", 60, "SellByTime"));
            for (int i = 0; i < 3; i++) c.ReplayTrades.Add(Trade("rare", 7, "TargetHit"));
            await c.SaveChangesAsync();
        }

        var result = await new EvidenceSurvivalOperation().ExecuteAsync(new EvidenceSurvivalArgs { Checkpoints = "15,30" }, Ctx());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok), result.Human ?? result.Message);
        var json = JsonSerializer.SerializeToElement(result.Data);
        var group = json.GetProperty("Groups")[0];
        Assert.That(json.GetProperty("Groups").GetArrayLength(), Is.EqualTo(1), "a 3-trade strategy gets no curves of its own");
        Assert.That(group.GetProperty("Strategy").GetString(), Is.EqualTo("gapper"));
        Assert.That(group.GetProperty("Censored").GetInt32(), Is.EqualTo(2));
        var at15 = group.GetProperty("Checkpoints")[0];
        Assert.That(at15.GetProperty("Target").GetDouble(), Is.EqualTo(0.6).Within(1e-4));
        Assert.That(at15.GetProperty("Stop").GetDouble(), Is.EqualTo(0).Within(1e-4));
        var at30 = group.GetProperty("Checkpoints")[1];
        Assert.That(at30.GetProperty("Stop").GetDouble(), Is.EqualTo(0.3).Within(1e-4));
        Assert.That(group.GetProperty("SuggestedSellByMinutes").GetDouble(), Is.EqualTo(10));
        Assert.That(result.Human, Does.Contain("advisory only").And.Contain("Cox for target"));
    }

    [Test]
    public async Task NoTrades_SaysHowToGetSome_AndBadCheckpointsAreAUsageError()
    {
        var empty = await new EvidenceSurvivalOperation().ExecuteAsync(new EvidenceSurvivalArgs(), Ctx());
        Assert.That(empty.Human, Does.Contain("No replay trades"));

        var bad = await new EvidenceSurvivalOperation().ExecuteAsync(new EvidenceSurvivalArgs { Checkpoints = "10,-5" }, Ctx());
        Assert.That(bad.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
