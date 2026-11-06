using IdiotProof.Core.Data;
using IdiotProof.Core.Monitoring.Formation;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Operations.Gapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper formation [status|enable|disable|universe] (IP-T443): reads the sink and the recorder's state, flips the settings.</summary>
public class GapperFormationOperationsTests
{
    private static readonly DateTime T = new(2026, 11, 6, 13, 30, 0, DateTimeKind.Utc); // 08:30 ET (EST)

    private sealed class Kit
    {
        public Guid Owner { get; } = Guid.NewGuid();
        public ServiceProvider Services { get; }
        public InMemoryGapperSnapshotSink Sink { get; } = new();
        public FormationState State { get; } = new();

        public Kit()
        {
            Services = new ServiceCollection()
                .AddLogging()
                .AddSingleton(TimeProvider.System)
                .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"formation-ops-{Guid.NewGuid():N}"))
                .AddSingleton<SettingsRepository>()
                .AddSingleton<FormationSettings>()
                .AddSingleton(State)
                .AddSingleton<IGapperSnapshotSink>(Sink)
                .AddSingleton(new FormationSchedule(FormationCatalogs.DefaultDecisionTimesEt))
                .AddSingleton(FormationTriggerRule.Never)
                .BuildServiceProvider();
        }

        public OperationContext Context() => new() { Services = Services, OwnerId = Owner, Clock = new Fixtures.FixedClock(new DateTimeOffset(T)), Width = 200 };

        public SettingsRepository Settings => Services.GetRequiredService<SettingsRepository>();
    }

    private static FormationSnapshotRow Row(string symbol, DateTime decisionUtc, double gap, bool passed = false, string reason = "schedule") =>
        FormationSnapshotRow.FromSnapshot(new GapperFormationSnapshot
        {
            Symbol = symbol, DateEt = new DateOnly(2026, 11, 6), DecisionUtc = decisionUtc, GapPct = gap, PmCumVol = 1000, VolRatioTod = 4.2,
            Velocity1m = 0.5, Velocity3m = 0.25, Velocity5m = -0.1, Acceleration = 0.05, DistToPmHighPct = 2.5, CatalystScore = 71, CatalystAgeMinutes = 90, Halted = false,
        }, FormationSchedule.Label(MinuteBarMath.ToEastern(decisionUtc).TimeOfDay), reason, passed, decisionUtc);

    [Test]
    public async Task Formation_ListsRecentRows_NewestFirst_InEt_AndFiltersBySymbol()
    {
        var kit = new Kit();
        await kit.Sink.WriteAsync([Row("GAPR", T, 30.0, passed: true), Row("QUIET", T, 0.4), Row("GAPR", T.AddMinutes(5), 31.2, reason: "gated")]);

        var all = await new GapperFormationOperation().ExecuteAsync(new GapperFormationArgs(), kit.Context());
        Assert.That(all.IsOk, Is.True, all.Message);
        var lines = all.Human!.Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(lines.First(l => l.Contains("GAPR")), Does.Contain("11-06 08:35").And.Contain("gated").And.Contain("+31.2"), "newest first, ET");
            Assert.That(lines.Single(l => l.Contains("QUIET")), Does.Contain("08:30").And.Contain("+0.4").And.Contain("71/90m"));
            Assert.That(lines.Count(l => l.Contains("PASS")), Is.EqualTo(1));
            Assert.That(all.Human, Does.Contain("3 row(s), 1 passed the trigger."));
        });

        var one = await new GapperFormationOperation().ExecuteAsync(new GapperFormationArgs { Symbol = "quiet" }, kit.Context());
        Assert.That(one.Human, Does.Contain("QUIET").And.Not.Contain("GAPR"));

        var none = await new GapperFormationOperation().ExecuteAsync(new GapperFormationArgs { Date = new DateOnly(2026, 11, 5) }, kit.Context());
        Assert.That(none.Human, Does.StartWith("No formation snapshots recorded on 2026-11-05"));
        Assert.That((await new GapperFormationOperation().ExecuteAsync(new GapperFormationArgs { Last = 0 }, kit.Context())).Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public async Task Status_Enable_Disable_RoundTripTheSettings()
    {
        var kit = new Kit();
        var off = await new GapperFormationStatusOperation().ExecuteAsync(new NoArgs(), kit.Context());
        Assert.That(off.IsOk, Is.True, off.Message);
        Assert.That(off.Human, Does.Contain("Enabled:").And.Contain("no (`gapper formation enable`").And.Contain("never (every threshold is null)").And.Contain("04:00 06:00 07:00 08:00 08:30").And.Contain("in-memory ring"));

        var enabled = await new GapperFormationEnableOperation().ExecuteAsync(new NoArgs(), kit.Context());
        Assert.That(enabled.IsOk, Is.True, enabled.Message);
        Assert.That(await kit.Settings.GetAsync(FormationSettings.EnabledKey), Is.EqualTo("true"));
        Assert.That(await kit.Settings.GetAsync(FormationSettings.OwnerKey), Is.EqualTo(kit.Owner.ToString()));
        Assert.That(enabled.Human, Does.Contain("never arms"));

        var on = await new GapperFormationStatusOperation().ExecuteAsync(new NoArgs(), kit.Context());
        Assert.That(on.Human, Does.Match(@"Enabled:\s+yes").And.Contain(kit.Owner.ToString()));

        var disabled = await new GapperFormationDisableOperation().ExecuteAsync(new NoArgs(), kit.Context());
        Assert.That(disabled.IsOk, Is.True, disabled.Message);
        Assert.That(await kit.Settings.GetAsync(FormationSettings.EnabledKey), Is.EqualTo("false"));
    }

    [Test]
    public async Task Enable_RefusesANonPaperBrokerMode()
    {
        var kit = new Kit();
        await kit.Settings.SetAsync(FormationSettings.BrokerModeKey, "live");
        var r = await new GapperFormationEnableOperation().ExecuteAsync(new NoArgs(), kit.Context());
        Assert.That(r.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(await kit.Settings.GetAsync(FormationSettings.EnabledKey), Is.Null);
    }

    [Test]
    public async Task Universe_ShowsWhyEachSymbolIsIn()
    {
        var kit = new Kit();
        var empty = await new GapperFormationUniverseOperation().ExecuteAsync(new NoArgs(), kit.Context());
        Assert.That(empty.Human, Does.StartWith("No formation universe built"));

        kit.State.SetUniverse([
            new FormationMember("ABCD", ["catalyst", "mover"], 66, 42.5, 3.4, 10_000_000),
            new FormationMember("EFGH", ["tracked"], null, null, 1.2, null),
        ], T, "2 tracked, 1 with catalysts");
        var r = await new GapperFormationUniverseOperation().ExecuteAsync(new NoArgs(), kit.Context());
        Assert.That(r.IsOk, Is.True, r.Message);
        Assert.Multiple(() =>
        {
            Assert.That(r.Human, Does.Contain("2 symbol(s)").And.Contain("2026-11-06 8:30 AM ET"));
            Assert.That(r.Human!.Split('\n').Single(l => l.Contains("ABCD")), Does.Contain("catalyst+mover").And.Contain("66").And.Contain("+42.5").And.Contain("$3.40").And.Contain("$34M"));
            Assert.That(r.Human.Split('\n').Single(l => l.Contains("EFGH")), Does.Contain("tracked").And.Contain("—"));
        });
    }
}
