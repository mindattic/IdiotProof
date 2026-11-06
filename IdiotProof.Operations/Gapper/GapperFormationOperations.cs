using System.Globalization;
using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Monitoring.Formation;
using IdiotProof.Core.Services;
using IdiotProof.Operations.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Gapper;

/// <summary>Shared pieces of the <c>gapper formation</c> operations (IP-T443).</summary>
internal static class FormationViews
{
    public static string Num(double? v, int decimals = 1) =>
        v is { } d ? d.ToString("0." + new string('0', decimals), CultureInfo.InvariantCulture) : "—";

    public static string Signed(double? v, int decimals = 2) =>
        v is { } d ? d.ToString("+0." + new string('0', decimals) + ";-0." + new string('0', decimals), CultureInfo.InvariantCulture) : "—";

    public static string Time(DateTime utc) => EasternClock.ToEastern(utc).ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Catalyst(GapperFormationSnapshotRow r) =>
        r.CatalystScore is { } s ? $"{s:0}{(r.CatalystAgeMinutes is { } a ? $"/{a:0}m" : "")}" : "—";

    public static string Table(OperationContext ctx, IReadOnlyList<GapperFormationSnapshotRow> rows)
    {
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("time ET").Column("sym").Column("why").Column("gap%", Align.Right).Column("vol×", Align.Right)
            .Column("v1", Align.Right).Column("v3", Align.Right).Column("v5", Align.Right).Column("accel", Align.Right)
            .Column("pmHigh%", Align.Right).Column("cat", Align.Right).Column("halt").Column("trig");
        foreach (var r in rows)
            table.Row(Time(r.DecisionUtc), r.Symbol, r.Reason, Signed(r.GapPct, 1), Num(r.VolRatioTod ?? r.OwnProfileRatio),
                Signed(r.Velocity1m), Signed(r.Velocity3m), Signed(r.Velocity5m), Signed(r.Acceleration),
                Num(r.DistToPmHighPct), Catalyst(r), r.Halted is { } h ? (h ? "HALT" : "no") : "?", r.TriggerPassed ? "PASS" : "");
        return table.Render();
    }

    public static object Data(GapperFormationSnapshotRow r) => new
    {
        r.Source, r.Symbol, r.DateEt, r.DecisionTimeEt, DecisionEt = EasternClock.ToEasternOffset(r.DecisionUtc), r.DecisionUtc, r.Reason,
        r.GapPct, r.PmCumVol, r.VolRatioTod, r.OwnProfileRatio, r.Velocity1m, r.Velocity3m, r.Velocity5m, r.Velocity15m, r.Acceleration, r.VolumeRoc,
        r.TradesProxy, r.SpreadProxy, r.DistToPmHighPct, r.MinsSinceFirstAbnormal, r.SessionVwap,
        r.AbnormalReturnD1, r.AbnormalReturnD2, r.AbnormalReturnD3, r.AbnormalReturnD4, r.AbnormalReturnD5,
        r.VolumeRatioD1, r.VolumeRatioD2, r.VolumeRatioD3, r.VolumeRatioD4, r.VolumeRatioD5,
        r.DaysSinceReverseSplit, r.DaysSince424B, r.DaysSinceS1, r.CatalystScore, r.CatalystAgeMinutes, r.CatalystDirection, r.CatalystPreClass,
        r.Halted, r.ExtraJson, r.FeatureVersion, r.TriggerPassed, r.ArmedStrategyId, r.WrittenUtc,
    };
}

public sealed record GapperFormationArgs
{
    [Option("symbol", Description = "Only this ticker.", Short = 's')]
    public string? Symbol { get; init; }

    [Option("date", Description = "ET trading day (yyyy-MM-dd); default every day the sink holds.", Short = 'd')]
    public DateOnly? Date { get; init; }

    [Option("last", Description = "How many rows, newest first (default 40, max 500).")]
    public int Last { get; init; } = 40;
}

[Operation("gapper formation", Summary = "Recent live gapper-formation snapshot rows: time ET, gap, volume ratio, 1/3/5-minute velocity, acceleration, distance to the premarket high, catalyst score/age, halted and whether the trigger rule passed.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper formation")]
[Example("idiotproof gapper formation --symbol ABCD --date 2026-11-06")]
public sealed class GapperFormationOperation : IOperation<GapperFormationArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperFormationArgs args, OperationContext ctx)
    {
        if (args.Last is < 1 or > 500) return OperationResult.Usage("--last must be 1-500.");
        ctx.RequireOwner();
        var sink = ctx.Services.GetService<IGapperSnapshotSink>();
        if (sink is null) return OperationResult.Ok(Array.Empty<object>(), "This host has no formation snapshot sink; run it where the Monitor is hosted.");

        var rows = await sink.ReadAsync(new GapperSnapshotQuery(args.Symbol, args.Date, args.Last), ctx.CancellationToken);
        var data = rows.Select(FormationViews.Data).ToList();
        if (rows.Count == 0)
            return OperationResult.Ok(data, "No formation snapshots recorded" + (args.Symbol is null ? "" : $" for {args.Symbol.ToUpperInvariant()}") + (args.Date is { } d ? $" on {d:yyyy-MM-dd}" : "") +
                ". The recorder writes them 04:00-09:45 ET when `gapper formation enable` is on and this Hub leads the Monitor.");
        var scope = (args.Symbol is null ? "" : $" {args.Symbol.ToUpperInvariant()}") + (args.Date is { } dd ? $" {dd:yyyy-MM-dd}" : "");
        return OperationResult.Ok(data, $"Formation snapshots{scope} (newest first; vol× = time-of-day ratio or own-profile ratio; cat = score/age min):\n" +
            FormationViews.Table(ctx, rows) + $"\n{rows.Count} row(s), {rows.Count(r => r.TriggerPassed)} passed the trigger.");
    }
}

[Operation("gapper formation status", Summary = "Whether the formation recorder is enabled, who owns it, whether this Hub leads, today's universe size, the stream, rows written, halted symbols, the trigger rule and the sink in use.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper formation status")]
public sealed class GapperFormationStatusOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var config = await ctx.Get<FormationSettings>().ReadAsync(ctx.CancellationToken);
        var hosted = ctx.Services.GetService<MonitorRuntimeState>() is not null;
        var state = ctx.Services.GetService<FormationState>()?.Snapshot();
        var rule = ctx.Services.GetService<FormationTriggerRule>() ?? FormationTriggerRule.Never;
        var schedule = ctx.Services.GetService<FormationSchedule>();
        var sink = ctx.Services.GetService<IGapperSnapshotSink>();

        var block = new KeyValueBlock()
            .Add("Enabled", config.Enabled ? "yes" : $"no (`gapper formation enable`, or set {FormationSettings.EnabledKey} = true)")
            .Add("Owner", config.OwnerUserId?.ToString() ?? $"unset ({FormationSettings.OwnerKey})")
            .Add("Broker mode", config.IsPaper ? "paper" : $"'{config.BrokerMode}' — NOT paper; the recorder refuses to run")
            .Add("Hosted", hosted ? "yes (this Hub hosts the Monitor)" : "no (the recorder runs with the Monitor)")
            .Add("Leader", state is null ? "—" : state.Leader ? "yes" : "no (standby)")
            .Add("Window", $"03:55-09:45 ET weekdays, tick {config.TickSeconds}s; recording from 04:00")
            .Add("Running", state is null ? "—" : state.Running ? $"yes since {Fmt.Et(state.StartedUtc)}" : $"no{(state.StopReason is null ? "" : $" ({state.StopReason})")}")
            .Add("Universe", state?.Universe.Count is > 0 ? $"{state.Universe.Count} symbol(s) built {Fmt.Et(state.UniverseBuiltUtc)}: {state.UniverseNote}" : $"none yet (price {Fmt.Money(config.PriceMin)}-{Fmt.Money(config.PriceMax)}, cap {config.MaxSymbols})")
            .Add("Record", $"schedule {(schedule is null ? "—" : string.Join(" ", schedule.DecisionTimesEt.Select(FormationSchedule.Label)))}; extra every {config.EveryMinutes} min when gap ≥{config.MinGap:0.#}% or vol ≥{config.MinVolRatio:0.#}×; control 1-in-{config.ControlOneIn} every {config.ControlEveryMinutes} min")
            .Add("Stream", state?.Stream ?? "—")
            .Add("Halted now", state is null ? null : state.HaltedSymbols.Count == 0 ? "none" : string.Join(", ", state.HaltedSymbols))
            .Add("TOD profile", config.TodProfilePath is null ? $"none ({FormationSettings.TodProfilePathKey}); VolRatioTod stays null" : $"{config.TodProfilePath} ({(state?.ProfileLoaded == true ? "loaded" : "not loaded")}){(config.TodDecileEdges.Count == 0 ? $"; no decile edges ({FormationSettings.TodDecileEdgesKey})" : $"; {config.TodDecileEdges.Count} decile edge(s)")}")
            .Add("Trigger", $"{rule.Describe()}; arming {(config.ArmingEnabled ? "enabled but not wired in this build" : $"disabled ({FormationSettings.ArmingEnabledKey})")}")
            .Add("Rows", state is null ? null : $"{state.RowsToday} today, {state.RowsTotal} since start, {state.TriggeredToday} trigger pass(es) today; last tick {Fmt.Et(state.LastTickUtc, "never")}")
            .Add("Sink", sink is null ? "none" : sink is InMemoryGapperSnapshotSink mem ? $"in-memory ring ({mem.Count} row(s) held, {mem.TotalWritten} written); SQL sink pending" : sink.GetType().Name)
            .Add("Retention", $"{config.RetentionDays} day(s); last prune {(state?.LastPruneDayEt is { } p ? p.ToString("yyyy-MM-dd") : "never")}")
            .Add("Scheduled auto", config.ScheduledAutoEt is { } at ? $"{FormationSchedule.Label(at)} ET (last run {(state?.ScheduledAutoLastRunEt is { } r ? r.ToString("yyyy-MM-dd") : "never")})" : $"off ({FormationSettings.ScheduledAutoEtKey})")
            .Add("Last error", state?.LastError is null ? null : $"{state.LastError} at {Fmt.Et(state.LastErrorUtc)}");

        var data = new
        {
            config.Enabled, config.OwnerUserId, config.BrokerMode, config.TickSeconds, Hosted = hosted,
            state?.Leader, state?.Running, state?.StartedUtc, state?.StoppedUtc, state?.StopReason,
            UniverseCount = state?.Universe.Count, state?.UniverseBuiltUtc, state?.UniverseNote,
            Schedule = schedule?.DecisionTimesEt.Select(FormationSchedule.Label).ToList(),
            config.EveryMinutes, config.MinGap, config.MinVolRatio, config.ControlEveryMinutes, config.ControlOneIn,
            state?.Stream, state?.HaltedSymbols, config.TodProfilePath, state?.ProfileLoaded,
            Trigger = rule, config.ArmingEnabled, state?.RowsToday, state?.RowsTotal, state?.TriggeredToday, state?.LastTickUtc,
            Sink = sink?.GetType().Name, config.RetentionDays, state?.LastPruneDayEt, config.ScheduledAutoEt, state?.ScheduledAutoLastRunEt,
            state?.LastError, state?.LastErrorUtc,
        };
        return OperationResult.Ok(data, block.Render());
    }
}

[Operation("gapper formation enable", Summary = "Turn the formation recorder on for you (paper-only research rows, 03:55-09:45 ET weekdays, with the Monitor leader); never arms.", Risk = OperationRisk.Write)]
[Example("idiotproof gapper formation enable")]
public sealed class GapperFormationEnableOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var settings = ctx.Get<SettingsRepository>();
        var mode = (await settings.GetAsync(FormationSettings.BrokerModeKey, ctx.CancellationToken) ?? "paper").Trim();
        if (!mode.Equals("paper", StringComparison.OrdinalIgnoreCase))
            return OperationResult.Blocked($"{FormationSettings.BrokerModeKey} is '{mode}', not paper. The recorder only ever runs against the paper route.");

        var existingOwner = await settings.GetAsync(FormationSettings.OwnerKey, ctx.CancellationToken);
        var ownerSet = Guid.TryParse(existingOwner, out var current) ? current : owner;
        await settings.SetAsync(FormationSettings.EnabledKey, "true", ctx.CancellationToken);
        await settings.SetAsync(FormationSettings.OwnerKey, ownerSet.ToString(), ctx.CancellationToken);
        ctx.Services.GetService<FormationSettings>()?.Invalidate();

        var note = ownerSet == owner ? "" : $" Owner stays {ownerSet} (already set).";
        return OperationResult.Ok(new { Enabled = true, OwnerUserId = ownerSet },
            $"Formation recorder enabled for {ownerSet}.{note} It runs with the Monitor leader 03:55-09:45 ET on trading days, writes research rows only, and never arms (trigger thresholds are null until the study sets them). `gapper formation status` shows it.");
    }
}

[Operation("gapper formation disable", Summary = "Turn the formation recorder off (settings only; nothing to disarm — it never arms).", Risk = OperationRisk.Write)]
[Example("idiotproof gapper formation disable")]
public sealed class GapperFormationDisableOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        ctx.RequireOwner();
        var settings = ctx.Get<SettingsRepository>();
        await settings.SetAsync(FormationSettings.EnabledKey, "false", ctx.CancellationToken);
        ctx.Services.GetService<FormationSettings>()?.Invalidate();
        return OperationResult.Ok(new { Enabled = false }, "Formation recorder disabled. The worker stops at its next tick (within a minute) and writes a formation-stop audit row.");
    }
}

[Operation("gapper formation universe", Summary = "Today's formation universe: every symbol the recorder watches and why it is in (catalyst, mover, most-active, tracked), with catalyst score, gap and price.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper formation universe")]
public sealed class GapperFormationUniverseOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        ctx.RequireOwner();
        var state = ctx.Services.GetService<FormationState>()?.Snapshot();
        var members = state?.Universe ?? [];
        var data = members.Select(m => new { m.Symbol, m.Reasons, m.CatalystScore, m.GapPct, m.Price, m.SharesOutstanding, m.MarketCap }).ToList();
        if (members.Count == 0)
            return Task.FromResult(OperationResult.Ok(data, "No formation universe built" + (state is null ? " (this host does not run the recorder)" : state.Config.Enabled ? " yet — it is built at 03:55 ET on trading days" : " — the recorder is disabled") + "."));

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("#", Align.Right).Column("sym").Column("why").Column("cat", Align.Right).Column("gap%", Align.Right).Column("price", Align.Right).Column("mktcap", Align.Right);
        var rank = 0;
        foreach (var m in members)
            table.Row((++rank).ToString(CultureInfo.InvariantCulture), m.Symbol, m.Why, FormationViews.Num(m.CatalystScore, 0), FormationViews.Signed(m.GapPct, 1),
                m.Price is { } p ? Fmt.Money(p) : "—", m.MarketCap is { } c ? Fmt.Money(c / 1_000_000, 0) + "M" : "—");
        return Task.FromResult(OperationResult.Ok(data, $"Formation universe, {members.Count} symbol(s), built {Fmt.Et(state!.UniverseBuiltUtc)} ({state.UniverseNote}):\n" + table.Render()));
    }
}
