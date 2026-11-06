using IdiotProof.Core.Data;
using IdiotProof.Core.Monitoring.Formation;
using IdiotProof.Evidence.Gappers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T443: the SQL sink writes live rows into the study's GapperFormationSnapshots table by column name,
/// packs the live-only fields into ExtraJson, upserts on (symbol, day, label) and prunes only live rows.
/// </summary>
[TestFixture]
public sealed class SqlGapperSnapshotSinkTests
{
    private static readonly DateTime T = new(2026, 11, 6, 13, 30, 0, DateTimeKind.Utc);

    private IDbContextFactory<AppDbContext> db = null!;
    private SqlGapperSnapshotSink sink = null!;

    [SetUp]
    public void SetUp()
    {
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"sql-sink-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        sink = new SqlGapperSnapshotSink(db);
    }

    private static FormationSnapshotRow Row(string symbol, DateTime decisionUtc, string reason = "schedule", bool passed = false, double gap = 12.5) =>
        FormationSnapshotRow.FromSnapshot(new GapperFormationSnapshot
        {
            Symbol = symbol, DateEt = DateOnly.FromDateTime(MinuteBarMath.ToEastern(decisionUtc)), DecisionUtc = decisionUtc,
            GapPct = gap, PmCumVol = 1234, Velocity3m = 0.7, CatalystScore = 55, CatalystStampIsUpperBound = true, Halted = false,
            Extra = new Dictionary<string, double> { ["overnightReturnD1"] = 1.5 },
        }, FormationSchedule.Label(MinuteBarMath.ToEastern(decisionUtc).TimeOfDay), reason, passed, decisionUtc.AddSeconds(3));

    [Test]
    public async Task Write_MapsEveryColumn_PacksLiveFieldsIntoExtraJson_AndReadsBack()
    {
        var armed = Guid.NewGuid();
        await sink.WriteAsync([Row("GAPR", T, "gated", passed: true) with { ArmedStrategyId = armed }, Row("QUIET", T, "control")]);

        await using (var c = await db.CreateDbContextAsync())
        {
            var e = await c.GapperFormationSnapshots.SingleAsync(x => x.Symbol == "GAPR");
            Assert.Multiple(() =>
            {
                Assert.That(e.Source, Is.EqualTo("live"));
                Assert.That(e.Split, Is.EqualTo("live"));
                Assert.That(e.DecisionLabel, Is.EqualTo("08:30"));
                Assert.That(e.DecisionTimeEt, Is.EqualTo(new TimeOnly(8, 30)));
                Assert.That(e.DecisionUtc, Is.EqualTo(T));
                Assert.That(e.GapPct, Is.EqualTo(12.5));
                Assert.That(e.Velocity3m, Is.EqualTo(0.7));
                Assert.That(e.CatalystScore, Is.EqualTo(55));
                Assert.That(e.CatalystStampIsUpperBound, Is.True);
                Assert.That(e.IsCandidate, Is.True);
                Assert.That(e.IsControl, Is.False);
                Assert.That(e.FeatureVersion, Is.EqualTo(FormationSnapshotRow.CurrentFeatureVersion));
                Assert.That(e.ComputedUtc, Is.EqualTo(T.AddSeconds(3)));
                Assert.That(e.ExtraJson, Does.Contain("\"overnightReturnD1\":1.5").And.Contain("\"reason\":\"gated\"").And.Contain("\"triggerPassed\":true").And.Contain(armed.ToString()));
            });
            var control = await c.GapperFormationSnapshots.SingleAsync(x => x.Symbol == "QUIET");
            Assert.That(control.IsControl, Is.True);
            Assert.That(control.IsCandidate, Is.False);
        }

        var back = (await sink.ReadAsync(new GapperSnapshotQuery("gapr"))).Single();
        Assert.Multiple(() =>
        {
            Assert.That(back.Reason, Is.EqualTo("gated"));
            Assert.That(back.TriggerPassed, Is.True);
            Assert.That(back.ArmedStrategyId, Is.EqualTo(armed));
            Assert.That(back.WrittenUtc, Is.EqualTo(T.AddSeconds(3)));
            Assert.That(back.ExtraJson, Is.EqualTo("{\"overnightReturnD1\":1.5}"), "the live-only keys are stripped on the way out");
            Assert.That(back.DecisionTimeEt, Is.EqualTo("08:30"));
            Assert.That(back.DecisionUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(back.CatalystStampIsUpperBound, Is.True);
        });
    }

    [Test]
    public async Task Write_UpsertsOnSymbolDayLabel_SoARestartNeverDuplicates()
    {
        await sink.WriteAsync([Row("GAPR", T, gap: 10)]);
        await sink.WriteAsync([Row("GAPR", T, gap: 11), Row("GAPR", T.AddMinutes(5), "gated")]);

        await using var c = await db.CreateDbContextAsync();
        var rows = await c.GapperFormationSnapshots.OrderBy(x => x.DecisionUtc).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].GapPct, Is.EqualTo(11), "the later write updates the stored row");
    }

    [Test]
    public async Task Read_FiltersByDateAndLimit_AndPrune_TouchesOnlyLiveRows()
    {
        await sink.WriteAsync([Row("A", T.AddDays(-40)), Row("A", T.AddDays(-1)), Row("B", T)]);
        await using (var c = await db.CreateDbContextAsync())
        {
            c.GapperFormationSnapshots.Add(new GapperFormationSnapshotRow { Source = "backtest", Symbol = "A", DateEt = new DateOnly(2024, 1, 2), DecisionLabel = "08:30", DecisionUtc = T.AddYears(-2), Split = "train" });
            await c.SaveChangesAsync();
        }

        Assert.That(await sink.ReadAsync(new GapperSnapshotQuery()), Has.Count.EqualTo(3), "backtest rows are not live rows");
        Assert.That((await sink.ReadAsync(new GapperSnapshotQuery(DateEt: new DateOnly(2026, 11, 6)))).Select(r => r.Symbol), Is.EqualTo(new[] { "B" }));
        Assert.That(await sink.ReadAsync(new GapperSnapshotQuery(Limit: 1)), Has.Count.EqualTo(1));

        Assert.That(await sink.PruneAsync(T.AddDays(-30)), Is.EqualTo(1));
        await using var check = await db.CreateDbContextAsync();
        Assert.That(await check.GapperFormationSnapshots.CountAsync(), Is.EqualTo(3), "two live rows and the backtest row remain");
    }

    [Test]
    public void UnpackExtra_ToleratesBadOrMissingJson()
    {
        Assert.That(SqlGapperSnapshotSink.UnpackExtra(null).Extra, Is.EqualTo("{}"));
        Assert.That(SqlGapperSnapshotSink.UnpackExtra("not json").Reason, Is.Null);
        var (extra, reason, passed, armed, written) = SqlGapperSnapshotSink.UnpackExtra("{\"x\":1}");
        Assert.Multiple(() =>
        {
            Assert.That(extra, Is.EqualTo("{\"x\":1}"));
            Assert.That(reason, Is.Null);
            Assert.That(passed, Is.False);
            Assert.That(armed, Is.Null);
            Assert.That(written, Is.Null);
        });
    }
}
