using System.Text.Json;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Monitoring.Formation;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Monitor.Tests;

/// <summary>
/// IP-T443: <see cref="GapperFormationScanner"/> writes one row per symbol per decision time (catching up missed
/// schedule times as of their own instant, never twice), extra rows only for symbols that clear the gap/volume gate
/// or sit in the deterministic control subset, flags the trigger rule and writes a single formation-blocked audit
/// row per symbol-day, carries the halt state from a status frame, and the retention helper prunes once per day.
/// Bars come from a scripted source, catalysts and audit rows from an in-memory database; nothing reaches Alpaca.
/// </summary>
[TestFixture]
public sealed class GapperFormationScannerTests
{
    // Friday 2026-11-06 (EST): 04:00 ET = 09:00 UTC.
    private static readonly DateOnly Day = new(2026, 11, 6);
    private static readonly DateTime SessionStart = new(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc);

    private IDbContextFactory<AppDbContext> db = null!;
    private BarSource bars = null!;
    private InMemoryGapperSnapshotSink sink = null!;
    private FormationState state = null!;
    private Guid owner;

    [SetUp]
    public void SetUp()
    {
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"formation-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        bars = new BarSource();
        sink = new InMemoryGapperSnapshotSink();
        state = new FormationState();
        state.EnsureDay(Day);
        owner = Guid.NewGuid();
        bars.Script("GAPR", priorClose: 1.00m, premarketPrice: 1.30m, minuteVolume: 200);
        bars.Script("QUIET", priorClose: 2.00m, premarketPrice: 2.01m, minuteVolume: 5);
    }

    private static DateTime Et(int hour, int minute, int second = 0) => SessionStart.AddHours(hour - 4).AddMinutes(minute).AddSeconds(second);

    private static FormationConfig Config(int controlOneIn = 10) => new()
    {
        Enabled = true, EveryMinutes = 5, MinGap = 10, MinVolRatio = 3, ControlEveryMinutes = 15, ControlOneIn = controlOneIn, RetentionDays = 30,
    };

    private static IReadOnlyList<FormationMember> Universe(params string[] symbols) =>
        symbols.Select(s => new FormationMember(s, ["mover"], null, null, null, null)).ToList();

    private GapperFormationScanner Scanner(FormationSchedule? schedule = null, FormationTriggerRule? rule = null) => new(
        bars, new CatalystLookup(db), sink, schedule ?? new FormationSchedule(FormationCatalogs.DefaultDecisionTimesEt), rule ?? FormationTriggerRule.Never,
        state, new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance), TimeProvider.System, NullLogger<GapperFormationScanner>.Instance);

    private static FormationSchedule LateOnly() => new([new TimeSpan(9, 45, 0)]);

    private async Task<List<FormationSnapshotRow>> RowsAsync(string? symbol = null) =>
        (await sink.ReadAsync(new GapperSnapshotQuery(symbol, null, 1000))).OrderBy(r => r.DecisionUtc).ThenBy(r => r.Symbol).ToList();

    private async Task<List<AuditLog>> AuditAsync(string category)
    {
        await using var c = await db.CreateDbContextAsync();
        return await c.AuditLogs.AsNoTracking().Where(a => a.Category == category).ToListAsync();
    }

    private async Task CatalystAsync(string symbol, DateTime publishedUtc, double score)
    {
        await using var c = await db.CreateDbContextAsync();
        var e = new CatalystEvent
        {
            Source = "alpaca-news", ExternalId = Guid.NewGuid().ToString("N"), PublishedUtc = publishedUtc, ReceivedUtc = publishedUtc,
            PrimarySymbol = symbol, SymbolsJson = JsonSerializer.Serialize(new[] { symbol }), Headline = "news", PreClass = "Fda", Direction = 1, CatalystScore = score, SourceTier = 2,
        };
        c.CatalystEvents.Add(e);
        c.CatalystEventSymbols.Add(new CatalystEventSymbol { EventId = e.Id, Symbol = symbol });
        await c.SaveChangesAsync();
    }

    [Test]
    public async Task CrossingADecisionTime_WritesOneRowPerSymbol_AsOfThatInstant_AndNeverAgain()
    {
        await CatalystAsync("GAPR", Et(7, 0), 66);
        var scanner = Scanner();

        var first = await scanner.TickAsync(Et(8, 30, 5), Universe("GAPR", "QUIET"), Config(), null, owner, CancellationToken.None);
        var rows = await RowsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(first.RowsWritten, Is.EqualTo(10), "five schedule times up to 08:30, two symbols");
            Assert.That(rows.Where(r => r.Symbol == "GAPR").Select(r => r.DecisionTimeEt), Is.EqualTo(new[] { "04:00", "06:00", "07:00", "08:00", "08:30" }));
            Assert.That(rows, Has.All.Matches<FormationSnapshotRow>(r => r.Reason == "schedule" && r.Source == "live"));
            Assert.That(rows.Single(r => r.Symbol == "GAPR" && r.DecisionTimeEt == "08:30").DecisionUtc, Is.EqualTo(Et(8, 30)), "exactly the schedule instant, not the tick");
            Assert.That(rows.Single(r => r.Symbol == "GAPR" && r.DecisionTimeEt == "08:30").GapPct, Is.EqualTo(30).Within(0.01));
            Assert.That(rows.Single(r => r.Symbol == "QUIET" && r.DecisionTimeEt == "08:30").GapPct, Is.EqualTo(0.5).Within(0.01));
            Assert.That(rows.Single(r => r.Symbol == "GAPR" && r.DecisionTimeEt == "08:30").CatalystAgeMinutes, Is.EqualTo(90).Within(0.01), "the 07:00 catalyst is 90 minutes old at 08:30");
            Assert.That(rows.Single(r => r.Symbol == "GAPR" && r.DecisionTimeEt == "06:00").CatalystScore, Is.Null, "the 07:00 catalyst is not knowable at 06:00");
            Assert.That(rows, Has.All.Matches<FormationSnapshotRow>(r => !r.TriggerPassed));
        });

        var second = await scanner.TickAsync(Et(8, 30, 20), Universe("GAPR", "QUIET"), Config(), null, owner, CancellationToken.None);
        var third = await scanner.TickAsync(Et(8, 31, 5), Universe("GAPR", "QUIET"), Config(), null, owner, CancellationToken.None);
        Assert.That(second.RowsWritten + third.RowsWritten, Is.Zero, "the same decision times are never written twice");
        Assert.That(await RowsAsync(), Has.Count.EqualTo(10));
    }

    [Test]
    public async Task ExtraRows_OnlyForSymbolsThatClearTheGate_AndOnlyOnTheCadence()
    {
        var scanner = Scanner(LateOnly());
        var on = await scanner.TickAsync(Et(8, 35, 10), Universe("GAPR", "QUIET"), Config(controlOneIn: 1_000_000), null, owner, CancellationToken.None);
        var rows = await RowsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(on.Computed, Is.EqualTo(2), "both symbols are evaluated");
            Assert.That(rows.Select(r => r.Symbol), Is.EqualTo(new[] { "GAPR" }), "only the 30% gapper clears gap >= 10% or vol >= 3x");
            Assert.That(rows[0].Reason, Is.EqualTo("gated"));
            Assert.That(rows[0].DecisionTimeEt, Is.EqualTo("08:35"));
            Assert.That(rows[0].DecisionUtc, Is.EqualTo(Et(8, 35)), "the extra row is as of the minute, not the tick second");
        });

        var off = await scanner.TickAsync(Et(8, 36, 10), Universe("GAPR", "QUIET"), Config(controlOneIn: 1_000_000), null, owner, CancellationToken.None);
        Assert.That(off.Computed, Is.Zero, "08:36 is not on the 5-minute cadence");
        var repeat = await scanner.TickAsync(Et(8, 35, 40), Universe("GAPR", "QUIET"), Config(controlOneIn: 1_000_000), null, owner, CancellationToken.None);
        Assert.That(repeat.Computed, Is.Zero, "a minute already decided is not recomputed");
    }

    [Test]
    public async Task ControlSample_WritesTheDeterministicSubset_RegardlessOfTheGate()
    {
        var inControl = Enumerable.Range(0, 5000).Select(i => $"C{i:0000}").First(s => FormationControlSample.IsControl(s, Day, 10));
        var notControl = Enumerable.Range(0, 5000).Select(i => $"N{i:0000}").First(s => !FormationControlSample.IsControl(s, Day, 10));
        var gapperNotControl = Enumerable.Range(0, 5000).Select(i => $"G{i:0000}").First(s => !FormationControlSample.IsControl(s, Day, 10));
        bars.Script(inControl, priorClose: 2.00m, premarketPrice: 2.01m, minuteVolume: 5);
        bars.Script(notControl, priorClose: 2.00m, premarketPrice: 2.01m, minuteVolume: 5);
        bars.Script(gapperNotControl, priorClose: 1.00m, premarketPrice: 1.30m, minuteVolume: 200);

        var scanner = Scanner(LateOnly());
        await scanner.TickAsync(Et(8, 45, 3), Universe(inControl, notControl, gapperNotControl), Config(), null, owner, CancellationToken.None);
        var rows = await RowsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => (r.Symbol, r.Reason)), Is.EquivalentTo(new[] { (inControl, "control"), (gapperNotControl, "gated") }),
                "a quiet control symbol is written regardless of the gate; a quiet non-control one is not; a gapping non-control one is written as gated");
            Assert.That(rows, Has.All.Matches<FormationSnapshotRow>(r => r.DecisionTimeEt == "08:45"));
        });
    }

    [Test]
    public async Task TriggerRule_FlagsTheRow_AndWritesOneBlockedAuditPerSymbolDay_NeverArming()
    {
        var rule = new FormationTriggerRule(MinGapPct: 10);
        var scanner = Scanner(LateOnly(), rule);
        await scanner.TickAsync(Et(8, 35, 2), Universe("GAPR", "QUIET"), Config(controlOneIn: 1_000_000), null, owner, CancellationToken.None);
        await scanner.TickAsync(Et(8, 40, 2), Universe("GAPR", "QUIET"), Config(controlOneIn: 1_000_000), null, owner, CancellationToken.None);

        var rows = await RowsAsync("GAPR");
        var blocked = await AuditAsync(GapperFormationScanner.BlockedAudit);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows, Has.All.Matches<FormationSnapshotRow>(r => r.TriggerPassed && r.ArmedStrategyId is null));
            Assert.That(blocked, Has.Count.EqualTo(1), "one audit row per symbol per day");
            Assert.That(blocked[0].UserId, Is.EqualTo(owner));
            Assert.That(blocked[0].Message, Does.Contain("GAPR passed the formation trigger at 08:35 ET").And.Contain("NOT armed").And.Contain(FormationSettings.ArmingEnabledKey).And.Contain("Plan penny:"));
        });
        await using var c = await db.CreateDbContextAsync();
        Assert.That(await c.Strategies.CountAsync(), Is.Zero, "the recorder never arms");
    }

    [Test]
    public async Task TheNeverRule_PassesNothing_AndAuditsNothing()
    {
        var scanner = Scanner(LateOnly());
        await scanner.TickAsync(Et(8, 35, 2), Universe("GAPR"), Config(controlOneIn: 1_000_000), null, owner, CancellationToken.None);
        Assert.That((await RowsAsync()).Single().TriggerPassed, Is.False);
        Assert.That(await AuditAsync(GapperFormationScanner.BlockedAudit), Is.Empty);
    }

    [Test]
    public async Task HaltedFlag_ComesFromTheStatusFrame_OnlyForCurrentRows()
    {
        var frame = JsonDocument.Parse("""{"T":"s","S":"GAPR","sc":"H","sm":"Trading Halt","rc":"T1","rm":"Halt News Pending","t":"2026-11-06T13:33:00Z","z":"C"}""").RootElement;
        var status = AlpacaStreamingClient.ParseStatus(frame)!;
        state.SetHalted(status.Symbol, status.Halted!.Value, status.TimestampUtc);

        var scanner = Scanner(new FormationSchedule([new TimeSpan(8, 0, 0), new TimeSpan(8, 35, 0)]));
        await scanner.TickAsync(Et(8, 35, 10), Universe("GAPR", "QUIET"), Config(controlOneIn: 1_000_000), null, owner, CancellationToken.None);
        var rows = await RowsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(rows.Single(r => r.Symbol == "GAPR" && r.DecisionTimeEt == "08:35").Halted, Is.True);
            Assert.That(rows.Single(r => r.Symbol == "QUIET" && r.DecisionTimeEt == "08:35").Halted, Is.Null, "no status frame named QUIET");
            Assert.That(rows.Single(r => r.Symbol == "GAPR" && r.DecisionTimeEt == "08:00").Halted, Is.Null, "a caught-up row older than the halt freshness carries no halt claim");
        });
    }

    [Test]
    public async Task Retention_PrunesThroughTheSink_OncePerDay()
    {
        var recording = new RecordingSink();
        var cfg = Config() with { RetentionDays = 30 };
        var now = Et(10, 0);
        var removed = await FormationRetention.PruneIfDueAsync(recording, state, cfg, now, CancellationToken.None);
        var again = await FormationRetention.PruneIfDueAsync(recording, state, cfg, now.AddMinutes(1), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(7));
            Assert.That(again, Is.Zero);
            Assert.That(recording.PruneCalls, Is.EqualTo(new[] { now.AddDays(-30) }));
            Assert.That(state.LastPruneDayEt, Is.EqualTo(Day));
        });
        var nextDay = await FormationRetention.PruneIfDueAsync(recording, state, cfg, now.AddDays(1), CancellationToken.None);
        Assert.That(nextDay, Is.EqualTo(7));
        Assert.That(recording.PruneCalls, Has.Count.EqualTo(2));
    }

    [Test]
    public void SupportingMath_Adv20_Decile_AndQualify()
    {
        var daily = Enumerable.Range(0, 30).Select(i => new Candle { Symbol = "X", StartUtc = SessionStart.AddDays(-40 + i), EndUtc = SessionStart.AddDays(-39 + i), Close = 1, Volume = i < 10 ? 1_000_000 : 500_000 }).ToList();
        Assert.That(GapperFormationScanner.Adv20(daily), Is.EqualTo(500_000));
        Assert.That(GapperFormationScanner.Adv20([]), Is.Null);
        Assert.That(GapperFormationScanner.Decile(750_000, [100_000, 500_000, 1_000_000]), Is.EqualTo(2));
        Assert.That(GapperFormationScanner.Decile(750_000, []), Is.Null);
        Assert.That(GapperFormationScanner.Decile(1e12, Enumerable.Range(1, 20).Select(i => i * 1e6).ToList()), Is.EqualTo(9), "clamped to the last decile");

        var quiet = new GapperFormationSnapshot { Symbol = "X", DateEt = Day, DecisionUtc = SessionStart, GapPct = 1, PmCumVol = 4_000_000 };
        Assert.That(GapperFormationScanner.Qualifies(quiet, 1_000_000, Config()), Is.True, "4x ADV premarket volume qualifies through the plain ratio");
        Assert.That(GapperFormationScanner.Qualifies(quiet with { PmCumVol = 100 }, 1_000_000, Config()), Is.False);
        Assert.That(GapperFormationScanner.Qualifies(quiet with { PmCumVol = 100, VolRatioTod = 3.5 }, 1_000_000, Config()), Is.True, "the time-of-day ratio wins when present");
    }

    private sealed class BarSource : IFormationBarSource
    {
        private readonly Dictionary<string, (List<Candle> Minutes, List<Candle> Daily)> scripts = new(StringComparer.OrdinalIgnoreCase);

        public void Script(string symbol, decimal priorClose, decimal premarketPrice, decimal minuteVolume)
        {
            var daily = Enumerable.Range(1, 25).Select(i =>
            {
                var start = SessionStart.AddDays(-i).Date.AddHours(5);
                return new Candle { Symbol = symbol, StartUtc = start, EndUtc = start.AddDays(1), Open = priorClose, High = priorClose * 1.05m, Low = priorClose * 0.95m, Close = priorClose, Volume = 1_000_000 };
            }).OrderBy(c => c.StartUtc).ToList();
            var minutes = Enumerable.Range(0, 16 * 60).Select(m =>
            {
                var start = SessionStart.AddMinutes(m);
                return new Candle { Symbol = symbol, StartUtc = start, EndUtc = start.AddMinutes(1), Open = premarketPrice, High = premarketPrice, Low = premarketPrice, Close = premarketPrice, Volume = minuteVolume };
            }).ToList();
            scripts[symbol] = (minutes, daily);
        }

        public Task<IReadOnlyList<Candle>> TodayMinuteBarsAsync(string symbol, DateOnly dateEt, DateTime nowUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Candle>>(scripts.TryGetValue(symbol, out var s) ? s.Minutes.Where(b => b.EndUtc <= nowUtc).ToList() : []);

        public Task<IReadOnlyList<Candle>> DailyBarsAsync(string symbol, DateOnly dateEt, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Candle>>(scripts.TryGetValue(symbol, out var s) ? s.Daily : []);

        public Task<IReadOnlyList<IReadOnlyList<Candle>>> PriorDayMinuteBarsAsync(string symbol, DateOnly dateEt, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IReadOnlyList<Candle>>>([]);
    }

    private sealed class RecordingSink : IGapperSnapshotSink
    {
        public List<DateTime> PruneCalls { get; } = [];

        public Task WriteAsync(IReadOnlyList<FormationSnapshotRow> rows, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<FormationSnapshotRow>> ReadAsync(GapperSnapshotQuery query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FormationSnapshotRow>>([]);

        public Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken ct = default)
        {
            PruneCalls.Add(olderThanUtc);
            return Task.FromResult(7);
        }
    }
}
