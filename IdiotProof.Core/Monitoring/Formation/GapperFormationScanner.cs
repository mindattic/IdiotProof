using System.Globalization;
using System.Text.Json;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring.Formation;

public sealed record FormationTickSummary(int Symbols, int Computed, int RowsWritten, int Triggered, IReadOnlyList<string> Errors);

/// <summary>
/// One recorder tick: for every universe symbol, the decision instants this tick owes (the
/// schedule times crossed and not yet written, the periodic gated minute, the control minute) are
/// computed with <see cref="GapperFormationFeatures.Compute"/> from the bars, daily history,
/// catalysts and halt state as of that instant, and written through the sink. A schedule time
/// missed while the worker was down is computed late but as of its own instant, so the row is
/// still point-in-time. Passing the trigger rule writes <c>TriggerPassed</c> and a
/// <c>formation-blocked</c> audit row; nothing here arms.
/// </summary>
public sealed class GapperFormationScanner(
    IFormationBarSource bars,
    CatalystLookup catalysts,
    IGapperSnapshotSink sink,
    FormationSchedule schedule,
    FormationTriggerRule trigger,
    FormationState state,
    AuditLogRepository audit,
    TimeProvider time,
    ILogger<GapperFormationScanner> log)
{
    public const string BlockedAudit = "formation-blocked";
    public static readonly TimeSpan HaltFreshness = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan CatalystLookback = TimeSpan.FromHours(24);
    public static readonly TimeSpan FirstDecisionEt = new(4, 0, 0);
    public static readonly TimeSpan LastDecisionEt = new(9, 45, 0);

    internal sealed record Pending(TimeSpan TimeEt, string Label, string Reason, bool ControlOnly);

    public async Task<FormationTickSummary> TickAsync(DateTime nowUtc, IReadOnlyList<FormationMember> universe, FormationConfig cfg,
        TimeOfDayProfile? profile, Guid? owner, CancellationToken ct)
    {
        var nowEt = MinuteBarMath.ToEastern(nowUtc);
        var dateEt = DateOnly.FromDateTime(nowEt);
        var pending = PendingDecisions(nowEt.TimeOfDay, cfg);
        var catalystsBySymbol = await CatalystsAsync(universe.Select(m => m.Symbol), nowUtc, ct);

        var rows = new List<FormationSnapshotRow>();
        var errors = new List<string>();
        int computed = 0, triggered = 0;

        foreach (var member in universe)
        {
            ct.ThrowIfCancellationRequested();
            var symbol = member.Symbol;
            var owed = Owed(symbol, dateEt, pending, cfg);
            if (owed.Count == 0) continue;
            try
            {
                var minuteBars = await bars.TodayMinuteBarsAsync(symbol, dateEt, nowUtc, ct);
                var daily = await bars.DailyBarsAsync(symbol, dateEt, ct);
                var prior = await bars.PriorDayMinuteBarsAsync(symbol, dateEt, ct);
                var adv20 = Adv20(daily);
                var decile = Decile(adv20, cfg.TodDecileEdges);
                catalystsBySymbol.TryGetValue(symbol, out var symbolCatalysts);

                foreach (var p in owed)
                {
                    if (!state.TryDecide(symbol, p.Label)) continue;
                    var t = MinuteBarMath.ToUtc(dateEt, p.TimeEt);
                    if (t > nowUtc) continue;
                    var inputs = new GapperFeatureInputs
                    {
                        Symbol = symbol,
                        DateEt = dateEt,
                        DecisionUtc = t,
                        MinuteBars = minuteBars,
                        DailyBars = daily,
                        Profile = profile,
                        AdvDecile = decile,
                        Adv20Shares = adv20,
                        PriorDayMinuteBars = prior,
                        Catalysts = symbolCatalysts ?? [],
                        Halted = nowUtc - t <= HaltFreshness ? state.Halted(symbol) : null,
                    };
                    var snap = GapperFormationFeatures.Compute(inputs);
                    computed++;
                    if (p.Reason == FormationSnapshotRow.GatedReason && !Qualifies(snap, adv20, cfg)) continue;

                    var passed = trigger.Passes(snap);
                    if (passed)
                    {
                        triggered++;
                        await BlockedAsync(symbol, snap, p.Label, minuteBars, daily, adv20, cfg, owner, ct);
                    }
                    rows.Add(FormationSnapshotRow.FromSnapshot(snap, p.Label, p.Reason, passed, Now));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors.Add($"{symbol}: {ex.Message}");
                log.LogWarning(ex, "Formation: {Symbol} failed this tick — continuing with the rest.", symbol);
            }
        }

        if (rows.Count > 0) await sink.WriteAsync(rows, ct);
        state.RecordTick(nowUtc, rows.Count);
        return new FormationTickSummary(universe.Count, computed, rows.Count, triggered, errors);
    }

    /// <summary>The decision instants any symbol may owe at this ET clock time, one per label.</summary>
    internal List<Pending> PendingDecisions(TimeSpan nowEt, FormationConfig cfg)
    {
        var byLabel = new Dictionary<string, Pending>(StringComparer.Ordinal);
        foreach (var t in schedule.DecisionTimesEt)
            if (t >= FirstDecisionEt && t <= nowEt)
                byLabel[FormationSchedule.Label(t)] = new Pending(t, FormationSchedule.Label(t), FormationSnapshotRow.ScheduleReason, false);

        var minute = new TimeSpan(nowEt.Hours, nowEt.Minutes, 0);
        if (minute >= FirstDecisionEt && minute <= LastDecisionEt)
        {
            var label = FormationSchedule.Label(minute);
            if (!byLabel.ContainsKey(label))
            {
                if (nowEt.Minutes % cfg.ControlEveryMinutes == 0)
                    byLabel[label] = new Pending(minute, label, FormationSnapshotRow.ControlReason, true);
                else if (nowEt.Minutes % cfg.EveryMinutes == 0)
                    byLabel[label] = new Pending(minute, label, FormationSnapshotRow.GatedReason, false);
            }
        }
        return byLabel.Values.OrderBy(p => p.TimeEt).ToList();
    }

    private List<Pending> Owed(string symbol, DateOnly dateEt, List<Pending> pending, FormationConfig cfg)
    {
        var owed = new List<Pending>();
        foreach (var p in pending)
        {
            if (state.IsDecided(symbol, p.Label)) continue;
            if (p.ControlOnly && !FormationControlSample.IsControl(symbol, dateEt, cfg.ControlOneIn))
            {
                if (p.TimeEt.Minutes % cfg.EveryMinutes == 0)
                    owed.Add(p with { Reason = FormationSnapshotRow.GatedReason, ControlOnly = false });
                continue;
            }
            owed.Add(p);
        }
        return owed;
    }

    internal static bool Qualifies(GapperFormationSnapshot snap, double? adv20, FormationConfig cfg)
    {
        if (snap.GapPct >= cfg.MinGap) return true;
        var ratio = snap.VolRatioTod ?? snap.OwnProfileRatio ?? (adv20 is > 0 ? snap.PmCumVol / adv20 : null);
        return ratio >= cfg.MinVolRatio;
    }

    internal static double? Adv20(IReadOnlyList<Candle> daily)
    {
        if (daily.Count == 0) return null;
        var last = daily.TakeLast(20).ToList();
        return (double)last.Average(c => c.Volume);
    }

    internal static int? Decile(double? adv20, IReadOnlyList<double> edges)
    {
        if (adv20 is not { } adv || edges.Count == 0) return null;
        return Math.Min(edges.Count(e => e <= adv), TimeOfDayProfile.DecileCount - 1);
    }

    private async Task<Dictionary<string, List<CatalystObservation>>> CatalystsAsync(IEnumerable<string> symbols, DateTime nowUtc, CancellationToken ct)
    {
        var result = new Dictionary<string, List<CatalystObservation>>(StringComparer.OrdinalIgnoreCase);
        var wanted = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return result;
        List<CatalystEvent> events;
        try { events = await catalysts.RecentAsync(wanted, nowUtc - CatalystLookback, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Formation: catalyst read failed; snapshots carry no catalyst this tick.");
            return result;
        }
        foreach (var e in events)
        {
            if (e.PublishedUtc > nowUtc) continue;
            var observation = new CatalystObservation(e.PublishedUtc, e.PreClass, e.Direction, e.SourceTier, e.CatalystScore,
                string.Equals(e.StampKind, "batch", StringComparison.OrdinalIgnoreCase));
            foreach (var s in SymbolsOf(e))
            {
                if (!wanted.Contains(s)) continue;
                if (!result.TryGetValue(s, out var list)) result[s] = list = [];
                list.Add(observation);
            }
        }
        return result;
    }

    private static IEnumerable<string> SymbolsOf(CatalystEvent e)
    {
        List<string>? symbols = null;
        try { symbols = JsonSerializer.Deserialize<List<string>>(e.SymbolsJson); }
        catch (JsonException) { }
        symbols ??= [];
        if (!string.IsNullOrWhiteSpace(e.PrimarySymbol)) symbols.Add(e.PrimarySymbol);
        return symbols.Select(s => s.Trim().ToUpperInvariant()).Distinct();
    }

    private async Task BlockedAsync(string symbol, GapperFormationSnapshot snap, string label, IReadOnlyList<Candle> minuteBars,
        IReadOnlyList<Candle> daily, double? adv20, FormationConfig cfg, Guid? owner, CancellationToken ct)
    {
        if (!state.TryMarkTriggered(symbol)) return;
        var price = minuteBars.Count > 0 ? (double)minuteBars[^1].Close : 0;
        string plan;
        try
        {
            var p = GapperPlanSynthesizer.Synthesize(symbol, new GapperPlanSignals(
                price, snap.GapPct ?? 0, daily.Count > 0 ? (double)daily[^1].Close : null, (long)snap.PmCumVol, adv20,
                snap.VolRatioTod ?? snap.OwnProfileRatio ?? (adv20 is > 0 ? snap.PmCumVol / adv20 : null), AtrPercent(daily)));
            plan = $"{p.Class}: stop {p.Profile.StopLossPercent:0.#}%, giveback {p.Profile.PeakGivebackPercent:0.#}% from {p.Profile.ArmExitAtEt}, sell by {p.Profile.SellByEt}, ${p.Profile.DefaultNotional:0}";
        }
        catch (Exception ex) { plan = $"plan not composable ({ex.Message})"; }

        var why = !cfg.ArmingEnabled ? $"{FormationSettings.ArmingEnabledKey} is false"
            : !cfg.IsPaper ? $"{FormationSettings.BrokerModeKey} is not paper"
            : "arming from formation data is not wired in this build";
        var message = $"{symbol} passed the formation trigger at {label} ET ({trigger.Describe()}): gap {Fmt(snap.GapPct)}%, volRatio {Fmt(snap.VolRatioTod ?? snap.OwnProfileRatio)}x, v3 {Fmt(snap.Velocity3m)}%/min, catalyst {Fmt(snap.CatalystScore)} — NOT armed: {why}. Plan {plan}.";
        var data = JsonSerializer.Serialize(new { symbol, label, snap.GapPct, snap.VolRatioTod, snap.OwnProfileRatio, snap.Velocity3m, snap.Acceleration, snap.DistToPmHighPct, snap.CatalystScore, price, plan, why });
        await audit.LogAsync(BlockedAudit, message, userId: owner, dataJson: data, ct: ct);
        log.LogInformation("Formation: {Message}", message);

        static string Fmt(double? v) => v is { } x ? x.ToString("0.##", CultureInfo.InvariantCulture) : "n/a";
    }

    internal static double? AtrPercent(IReadOnlyList<Candle> daily)
    {
        var recent = daily.TakeLast(15).Where(c => c.Close > 0).ToList();
        return recent.Count >= 3 ? recent.Average(c => (double)(c.High - c.Low) / (double)c.Close * 100.0) : null;
    }

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}
