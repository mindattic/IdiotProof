using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Core.Services;
using IdiotProof.Engine.Settings;
using IdiotProof.Evidence.Gappers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>
/// IP-T442 end to end on in-memory rows and a temp file store: snapshot writes Source=backtest rows
/// at every frozen decision time, VolRatioTod is null without a profile and set once tod-profile.json
/// exists; label writes one row per decision time with the next-bar entry.
/// </summary>
[TestFixture]
public class GapperSnapshotStageTests
{
    private static readonly DateOnly D = new(2026, 7, 17);
    private string studyDir = null!;
    private string barRoot = null!;
    private ServiceProvider services = null!;

    [SetUp]
    public void SetUp()
    {
        studyDir = Path.Combine(Path.GetTempPath(), "gapper-study-" + Guid.NewGuid().ToString("N"));
        barRoot = Path.Combine(studyDir, "bars");
        Directory.CreateDirectory(studyDir);
        var real = GapperStudyTestKit.Paths;
        File.Copy(real.ParamsFile, Path.Combine(studyDir, "params.json"));
        File.Copy(real.HypothesesFile, Path.Combine(studyDir, "hypotheses.json"));
        Environment.SetEnvironmentVariable(GapperStudyPaths.EnvVar, studyDir);
        Environment.SetEnvironmentVariable("GAPPER_STUDY_BAR_ROOT", barRoot);

        services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 7, 20, 18, 0, 0, TimeSpan.Zero)))
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"gapper-{Guid.NewGuid():N}"))
            .AddSingleton(new IdiotProofDatabase(@"Server=(localdb)\MSSQLLocalDB;Database=IdiotProof_Research_Test;"))
            .AddSingleton(new AppSettings())
            .AddSingleton<SettingsRepository>()
            .AddSingleton<GapperStudyServices>(sp => new GapperStudyServices(
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<IdiotProofDatabase>(), sp.GetRequiredService<AppSettings>(),
                null!, sp.GetRequiredService<SettingsRepository>(), sp.GetRequiredService<TimeProvider>(), NullLogger<GapperStudyServices>.Instance))
            .AddSingleton<GapperSnapshotStage>()
            .AddSingleton<GapperLabelStage>()
            .BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        services.Dispose();
        Environment.SetEnvironmentVariable(GapperStudyPaths.EnvVar, null);
        Environment.SetEnvironmentVariable("GAPPER_STUDY_BAR_ROOT", null);
        if (Directory.Exists(studyDir)) Directory.Delete(studyDir, true);
    }

    private async Task SeedAsync()
    {
        var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        db.GapperStudyUniverseDays.Add(new GapperStudyUniverseDay
        {
            Symbol = "GAP", DateEt = D, Split = GapperStudyServices.SmokeSplit, AdjPrevClose = 2.00m, Adv20Dollars = 400_000m, Adv20Shares = 200_000m,
            Sessions = 20, AdvDecile = 3, PriceTier = 1, IsCandidate = true, IsGapper = true, GapTier = "Tier20", GapperSubType = "premarket",
        });
        db.GapperStudyUniverseDays.Add(new GapperStudyUniverseDay
        {
            Symbol = "CTL", DateEt = D, Split = GapperStudyServices.SmokeSplit, AdjPrevClose = 2.00m, Adv20Dollars = 380_000m, Adv20Shares = 190_000m,
            Sessions = 20, AdvDecile = 3, PriceTier = 1, IsCandidate = false, ControlForSymbol = "GAP",
        });
        foreach (var d in MarketCalendar.PriorTradingDays(D, 25))
            foreach (var sym in new[] { "GAP", "CTL" })
                db.GapperStudyDailyBars.Add(new GapperStudyDailyBar { Symbol = sym, DateEt = d, Open = 2m, High = 2.05m, Low = 1.95m, Close = 2m, Volume = 200_000m, Feed = "Alpaca:sip" });
        await db.SaveChangesAsync();

        var store = new FileBackedBarStore(barRoot);
        store.AppendDay("GAP", D, Session(D, i => i < 240 ? 2.00 : 2.60, _ => 2_000));
        store.AppendDay("CTL", D, Session(D, _ => 2.00, i => i < 330 ? 50 : 800));
        foreach (var d in MarketCalendar.PriorTradingDays(D, 5))
            store.AppendDay("GAP", d, Session(d, _ => 2.00, i => i < 330 ? 50 : 800));
    }

    [Test]
    public async Task Snapshot_WritesBacktestRows_WithoutAProfile_ThenWithOne()
    {
        await SeedAsync();
        var stage = services.GetRequiredService<GapperSnapshotStage>();
        var paths = new GapperStudyPaths(studyDir);
        var schedule = Params.DecisionTimes();

        var first = await stage.RunAsync(new SnapshotStageRequest("train", Smoke: true, null, null), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(first.SymbolDays, Is.EqualTo(2));
            Assert.That(first.Rows, Is.EqualTo(2 * schedule.Count));
            Assert.That(first.ProfileBuilt, Is.True, "the smoke run fits its profile on the control day");
            Assert.That(File.Exists(paths.ProfileFile(true)), Is.True);
        });

        var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var rows = await db.GapperFormationSnapshots.AsNoTracking().Where(r => r.Symbol == "GAP").OrderBy(r => r.DecisionUtc).ToListAsync();
        var at0800 = rows.Single(r => r.DecisionLabel == "08:00");
        var at0830 = rows.Single(r => r.DecisionLabel == "08:30");
        var dayBefore = rows.Single(r => r.DecisionLabel == "D-1 16:00");
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.Source).Distinct(), Is.EqualTo(new[] { GapperSnapshotStage.Source }));
            Assert.That(rows, Has.Count.EqualTo(schedule.Count));
            Assert.That(rows.All(r => r.Split == GapperStudyServices.SmokeSplit && r.IsCandidate && !r.IsControl), Is.True);
            Assert.That(at0800.DecisionUtc, Is.EqualTo(Et(D, 8, 0)));
            Assert.That(at0800.GapPct, Is.EqualTo(0.0).Within(1e-6), "the bar starting at 08:00 has not ended at 08:00: no lookahead");
            Assert.That(at0830.GapPct, Is.EqualTo(30.0).Within(1e-6), "2.60 over 2.00 once the pop bars have closed");
            Assert.That(at0800.PmCumVol, Is.EqualTo(240 * 2_000.0));
            Assert.That(at0800.VolRatioTod, Is.Not.Null, "the profile was fit before the first ratio was computed");
            Assert.That(at0800.VolRatioTod, Is.GreaterThan(1.0));
            Assert.That(at0800.Velocity3m, Is.Not.Null);
            Assert.That(at0800.OwnProfileRatio, Is.Not.Null, "D-1..D-5 bars exist for GAP");
            Assert.That(at0800.ExtraJson, Does.Contain("priorDayVolumeRatio"));
            Assert.That(dayBefore.DecisionUtc, Is.EqualTo(Et(MarketCalendar.PreviousTradingDay(D), 16, 0)));
            Assert.That(dayBefore.PmCumVol, Is.EqualTo(0), "nothing of D is knowable the evening before");
            Assert.That(dayBefore.AbnormalReturnD1, Is.Not.Null);
        });
        var control = await db.GapperFormationSnapshots.AsNoTracking().Where(r => r.Symbol == "CTL").ToListAsync();
        Assert.That(control.All(r => r.IsControl && !r.IsCandidate), Is.True);
    }

    [Test]
    public async Task Snapshot_IsIdempotent_AndCarriesTheNewFeaturesInExtraJson()
    {
        await SeedAsync();
        var stage = services.GetRequiredService<GapperSnapshotStage>();
        var first = await stage.RunAsync(new SnapshotStageRequest("train", Smoke: true, null, null), CancellationToken.None);
        var second = await stage.RunAsync(new SnapshotStageRequest("train", Smoke: true, null, null), CancellationToken.None);
        var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var rows = await db.GapperFormationSnapshots.AsNoTracking().Where(r => r.Symbol == "GAP").ToListAsync();
        var at0945 = rows.Single(r => r.DecisionLabel == "09:45");
        var extras = GapperSnapshotExtras.Parse(at0945.ExtraJson);
        Assert.Multiple(() =>
        {
            Assert.That(second.Rows, Is.EqualTo(first.Rows));
            Assert.That(rows, Has.Count.EqualTo(Params.DecisionTimes().Count), "re-running replaces rows in place");
            Assert.That(extras.Number(GapperSnapshotExtras.TradesRoc), Is.EqualTo(0).Within(1e-12), "every minute prints the same trade count");
            Assert.That(extras.Has(GapperSnapshotExtras.Form4BuysD30), Is.False, "no CIK mapping: null, so absent");
            Assert.That(extras.Has(GapperSnapshotExtras.EightKItemCount), Is.False);
            Assert.That(extras.EightKItems(), Is.Null);
            Assert.That(at0945.TradesProxy, Is.EqualTo(15 * 3), "the kit's bars carry 3 trades a minute");
            Assert.That(GapperSnapshotStage.Registry(Params).IsPostRegistration(VelocityDeathNearPmHighFeature.FeatureName), Is.True);
        });
    }

    [Test]
    public async Task Snapshot_VolRatioTod_IsNull_WhenNoProfileCanBeFit()
    {
        await SeedAsync();
        var store = new FileBackedBarStore(barRoot);
        Directory.Delete(Path.Combine(barRoot, "CTL"), true);
        var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var ctl = await db.GapperStudyUniverseDays.SingleAsync(r => r.Symbol == "CTL");
        ctl.ControlForSymbol = null;
        await db.SaveChangesAsync();
        Directory.Delete(Path.Combine(barRoot, "GAP"), true);
        store.AppendDay("GAP", D, []);

        var r = await services.GetRequiredService<GapperSnapshotStage>().RunAsync(new SnapshotStageRequest("train", Smoke: true, null, null), CancellationToken.None);
        Assert.That(r.ProfileBuilt, Is.False);
        Assert.That(r.WithProfile, Is.EqualTo(0));
        var rows = await db.GapperFormationSnapshots.AsNoTracking().Where(x => x.Symbol == "GAP").ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(Params.DecisionTimes().Count));
        Assert.That(rows.All(x => x.VolRatioTod is null), Is.True);
    }

    [Test]
    public async Task Label_WritesOneRowPerDecisionTime_WithTheNextBarEntry()
    {
        await SeedAsync();
        var r = await services.GetRequiredService<GapperLabelStage>().RunAsync(new LabelStageRequest("train", Smoke: true, null, null), CancellationToken.None);
        Assert.That(r.Rows, Is.EqualTo(2 * Params.DecisionTimes().Count));
        var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var at0800 = await db.GapperStudyLabels.AsNoTracking().SingleAsync(l => l.Symbol == "GAP" && l.DecisionLabel == "08:00");
        Assert.Multiple(() =>
        {
            Assert.That(at0800.EntryUtc, Is.EqualTo(Et(D, 8, 0)), "entry is the first bar starting at or after t");
            Assert.That(at0800.EntryRef, Is.EqualTo(2.00m), "that bar opens at the prior close and pops inside the minute");
            Assert.That(at0800.Reach20By0945, Is.True);
            Assert.That(at0800.FwdMax1600, Is.EqualTo(0.30).Within(1e-9), "2.60 high over the 2.00 entry");
            Assert.That(at0800.HodBucket, Is.EqualTo("<09:15"));
            Assert.That(at0800.VelocityDeathTimeEt, Is.Null, "the pop happened before the 08:30 arm window");
        });
    }

    [Test]
    public void HoldoutSplit_RefusesWithoutTheUnlockFile()
    {
        var stage = services.GetRequiredService<GapperSnapshotStage>();
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => stage.RunAsync(new SnapshotStageRequest("holdout", Smoke: false, null, null), CancellationToken.None));
        Assert.That(ex!.Message, Does.StartWith("Holdout locked"));
    }
}
