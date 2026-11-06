using IdiotProof.Core.Monitoring.Formation;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Blazor.Tests;

/// <summary>IP-T443: the snapshot row mirrors every core column of the feature snapshot, and the in-memory sink bounds, filters and prunes.</summary>
[TestFixture]
public sealed class GapperSnapshotSinkTests
{
    private static readonly DateTime T = new(2026, 11, 6, 13, 30, 0, DateTimeKind.Utc);

    private static FormationSnapshotRow Row(string symbol, DateTime decisionUtc, string reason = "schedule", bool passed = false) =>
        FormationSnapshotRow.FromSnapshot(new GapperFormationSnapshot
        {
            Symbol = symbol, DateEt = DateOnly.FromDateTime(MinuteBarMath.ToEastern(decisionUtc)), DecisionUtc = decisionUtc,
            GapPct = 12.5, PmCumVol = 1234, Velocity3m = 0.7, Halted = false, Extra = new Dictionary<string, double> { ["overnightReturnD1"] = 1.5 },
        }, FormationSchedule.Label(MinuteBarMath.ToEastern(decisionUtc).TimeOfDay), reason, passed, decisionUtc);

    [Test]
    public void Row_MirrorsEveryCoreColumnOfTheSnapshot_ByNameAndType()
    {
        var snapshotProps = typeof(GapperFormationSnapshot).GetProperties().Where(p => p.Name != nameof(GapperFormationSnapshot.Extra)).ToList();
        var rowProps = typeof(FormationSnapshotRow).GetProperties().ToDictionary(p => p.Name);
        foreach (var p in snapshotProps)
        {
            Assert.That(rowProps.ContainsKey(p.Name), Is.True, $"row lacks {p.Name}");
            Assert.That(rowProps[p.Name].PropertyType, Is.EqualTo(p.PropertyType), $"row column {p.Name} type");
        }
        var row = Row("ABCD", T, passed: true);
        Assert.Multiple(() =>
        {
            Assert.That(row.Source, Is.EqualTo("live"));
            Assert.That(row.DecisionTimeEt, Is.EqualTo("08:30"));
            Assert.That(row.DateEt, Is.EqualTo(new DateOnly(2026, 11, 6)));
            Assert.That(row.GapPct, Is.EqualTo(12.5));
            Assert.That(row.ExtraJson, Does.Contain("\"overnightReturnD1\":1.5"));
            Assert.That(row.FeatureVersion, Is.EqualTo(FormationSnapshotRow.CurrentFeatureVersion));
            Assert.That(row.TriggerPassed, Is.True);
            Assert.That(row.ArmedStrategyId, Is.Null);
        });
    }

    [Test]
    public async Task InMemorySink_KeepsTheNewest_FiltersReads_AndPrunes()
    {
        var sink = new InMemoryGapperSnapshotSink(capacity: 5);
        var rows = Enumerable.Range(0, 8).Select(i => Row(i % 2 == 0 ? "EVEN" : "ODD", T.AddMinutes(i))).ToList();
        await sink.WriteAsync(rows);
        Assert.That(sink.Count, Is.EqualTo(5));
        Assert.That(sink.TotalWritten, Is.EqualTo(8));

        var all = await sink.ReadAsync(new GapperSnapshotQuery());
        Assert.That(all.Select(r => r.DecisionUtc), Is.Ordered.Descending);
        Assert.That(all[0].DecisionUtc, Is.EqualTo(T.AddMinutes(7)));

        var odd = await sink.ReadAsync(new GapperSnapshotQuery(Symbol: "odd"));
        Assert.That(odd.Select(r => r.Symbol), Is.All.EqualTo("ODD"));
        Assert.That(await sink.ReadAsync(new GapperSnapshotQuery(DateEt: new DateOnly(2026, 11, 5))), Is.Empty);
        Assert.That(await sink.ReadAsync(new GapperSnapshotQuery(Limit: 2)), Has.Count.EqualTo(2));

        var removed = await sink.PruneAsync(T.AddMinutes(6));
        Assert.That(removed, Is.EqualTo(3));
        Assert.That(sink.Count, Is.EqualTo(2));
    }
}
