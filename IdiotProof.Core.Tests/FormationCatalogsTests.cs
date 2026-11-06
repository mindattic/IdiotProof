using IdiotProof.Core.Catalogs;
using IdiotProof.Core.Monitoring.Formation;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Blazor.Tests;

/// <summary>IP-T443: the shipped formation catalogs — the schedule mirrors the study's decision times and the trigger rule is all null, so it never passes.</summary>
[TestFixture]
public sealed class FormationCatalogsTests
{
    private static readonly string[] StudyTimes = ["04:00", "06:00", "07:00", "08:00", "08:30", "09:00", "09:15", "09:25", "09:30", "09:31", "09:35", "09:45"];

    private static GapperFormationSnapshot Monster() => new()
    {
        Symbol = "ABCD", DateEt = new DateOnly(2026, 11, 6), DecisionUtc = new DateTime(2026, 11, 6, 13, 30, 0, DateTimeKind.Utc),
        GapPct = 45, PmCumVol = 2_000_000, VolRatioTod = 12, Velocity3m = 1.5, Acceleration = 0.4, DistToPmHighPct = 1.0, CatalystScore = 80,
    };

    [Test]
    public void ShippedSchedule_MirrorsTheStudy_AndSkipsThePriorCloseReference()
    {
        var schedule = FormationSchedule.Load(StaticCatalogSource.Default);
        Assert.That(schedule.DecisionTimesEt.Select(FormationSchedule.Label), Is.EqualTo(StudyTimes));
        Assert.That(FormationCatalogs.DefaultDecisionTimesEt.Select(FormationSchedule.Label), Is.EqualTo(StudyTimes), "the built-in fallback is the same list");
    }

    [Test]
    public void Schedule_Parse_DedupesAndSorts_AndRejectsAnEmptyList()
    {
        var s = FormationSchedule.Parse("""{"decisionTimesEt":["09:00","D-1 16:00","04:00","09:00"]}""");
        Assert.That(s.DecisionTimesEt.Select(FormationSchedule.Label), Is.EqualTo(new[] { "04:00", "09:00" }));
        Assert.Throws<InvalidDataException>(() => FormationSchedule.Parse("""{"decisionTimesEt":["D-1 16:00"]}"""));
    }

    [Test]
    public void ShippedTriggerRule_IsAllNull_AndNeverPasses()
    {
        var rule = FormationTriggerRule.Load(StaticCatalogSource.Default);
        Assert.That(rule.IsSet, Is.False);
        Assert.That(rule.Passes(Monster()), Is.False);
        Assert.That(rule.Describe(), Does.StartWith("never"));
        Assert.That(FormationTriggerRule.Never.Passes(Monster()), Is.False);
    }

    [Test]
    public void ASetRule_PassesTheSnapshotThatClearsIt_AndFailsClosedOnANullFeature()
    {
        var rule = FormationTriggerRule.Parse("""{"minGapPct": 10, "minVolRatioTod": 3, "minVelocity3m": 0.5, "requireCatalyst": true}""");
        Assert.That(rule.IsSet, Is.True);
        Assert.That(rule.Passes(Monster()), Is.True);
        Assert.That(rule.Passes(Monster() with { GapPct = 9.9 }), Is.False);
        Assert.That(rule.Passes(Monster() with { VolRatioTod = null }), Is.False, "a set threshold with no feature value fails closed");
        Assert.That(rule.Passes(Monster() with { CatalystScore = null }), Is.False, "requireCatalyst with no catalyst fails");
        Assert.That(new FormationTriggerRule(MaxGapPct: 40).Passes(Monster()), Is.False);
        Assert.That(new FormationTriggerRule(MaxDistFromPmHighPct: 2).Passes(Monster()), Is.True);
        Assert.That(rule.Describe(), Does.Contain("gap >= 10%").And.Contain("catalyst required"));
    }

    [Test]
    public void ControlSample_IsDeterministic_DayDependent_AndRoughlyOneInN()
    {
        var day = new DateOnly(2026, 11, 6);
        var symbols = Enumerable.Range(0, 2000).Select(i => $"S{i:0000}").ToList();
        var first = symbols.Where(s => FormationControlSample.IsControl(s, day, 10)).ToList();
        var again = symbols.Where(s => FormationControlSample.IsControl(s, day, 10)).ToList();
        var nextDay = symbols.Where(s => FormationControlSample.IsControl(s, day.AddDays(1), 10)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(again, Is.EqualTo(first));
            Assert.That(first, Has.Count.InRange(140, 260), "about 10% of 2000");
            Assert.That(nextDay, Is.Not.EqualTo(first));
            Assert.That(FormationControlSample.IsControl("ANY", day, 1), Is.True);
            Assert.That(FormationControlSample.IsControl("abcd", day, 10), Is.EqualTo(FormationControlSample.IsControl("ABCD", day, 10)));
        });
    }

    [Test]
    public void Settings_ParseHelpers_ReadTimesAndDecileEdges()
    {
        Assert.That(FormationSettings.ParseTime("03:55"), Is.EqualTo(new TimeSpan(3, 55, 0)));
        Assert.That(FormationSettings.ParseTime("nope"), Is.Null);
        Assert.That(FormationSettings.ParseTime(null), Is.Null);
        Assert.That(FormationSettings.ParseEdges("500000, 100000;2000000"), Is.EqualTo(new[] { 100000.0, 500000.0, 2000000.0 }));
        Assert.That(FormationSettings.ParseEdges(""), Is.Empty);
    }
}
