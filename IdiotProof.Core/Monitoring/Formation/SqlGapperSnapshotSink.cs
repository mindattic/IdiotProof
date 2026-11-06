using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>
/// The live recorder's sink over the study's <c>GapperFormationSnapshots</c> table, so live rows
/// (<c>Source = "live"</c>) land beside backtest rows column for column. The table has no
/// Reason/TriggerPassed/ArmedStrategyId/WrittenUtc columns, so those ride inside <c>ExtraJson</c>
/// under <c>reason</c>, <c>triggerPassed</c>, <c>armedStrategyId</c> and <c>writtenUtc</c>
/// (the table's <c>ComputedUtc</c> also carries the write instant). A (symbol, day, label) that is
/// already stored is updated, never duplicated, so a restart that re-crosses a decision time is
/// harmless.
/// </summary>
public sealed class SqlGapperSnapshotSink(IDbContextFactory<AppDbContext> dbFactory) : IGapperSnapshotSink
{
    public const string LiveSplit = "live";
    public const string ReasonKey = "reason";
    public const string TriggerPassedKey = "triggerPassed";
    public const string ArmedStrategyIdKey = "armedStrategyId";
    public const string WrittenUtcKey = "writtenUtc";

    public async Task WriteAsync(IReadOnlyList<FormationSnapshotRow> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var days = rows.Select(r => r.DateEt).Distinct().ToList();
        var symbols = rows.Select(r => r.Symbol).Distinct().ToList();
        var existing = await db.GapperFormationSnapshots
            .Where(e => e.Source == FormationSnapshotRow.LiveSource && days.Contains(e.DateEt) && symbols.Contains(e.Symbol))
            .ToListAsync(ct);
        var byKey = existing.ToDictionary(e => (e.Symbol, e.DateEt, e.DecisionLabel));

        foreach (var row in rows)
        {
            if (byKey.TryGetValue((row.Symbol, row.DateEt, row.DecisionTimeEt), out var entity)) Apply(row, entity);
            else db.GapperFormationSnapshots.Add(Apply(row, new GapperFormationSnapshotRow()));
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FormationSnapshotRow>> ReadAsync(GapperSnapshotQuery query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.GapperFormationSnapshots.AsNoTracking().Where(e => e.Source == FormationSnapshotRow.LiveSource);
        if (!string.IsNullOrWhiteSpace(query.Symbol))
        {
            var sym = query.Symbol.Trim().ToUpperInvariant();
            q = q.Where(e => e.Symbol == sym);
        }
        if (query.DateEt is { } d) q = q.Where(e => e.DateEt == d);
        var rows = await q.OrderByDescending(e => e.DecisionUtc).ThenBy(e => e.Symbol).Take(Math.Max(1, query.Limit)).ToListAsync(ct);
        return rows.Select(ToRow).ToList();
    }

    public async Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stale = await db.GapperFormationSnapshots
            .Where(e => e.Source == FormationSnapshotRow.LiveSource && e.DecisionUtc < olderThanUtc)
            .ToListAsync(ct);
        if (stale.Count == 0) return 0;
        db.GapperFormationSnapshots.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    internal static GapperFormationSnapshotRow Apply(FormationSnapshotRow r, GapperFormationSnapshotRow e)
    {
        e.Source = FormationSnapshotRow.LiveSource;
        e.Symbol = r.Symbol;
        e.DateEt = r.DateEt;
        e.DecisionLabel = r.DecisionTimeEt;
        e.DecisionTimeEt = TimeOnly.TryParseExact(r.DecisionTimeEt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t : TimeOnly.FromDateTime(IdiotProof.Evidence.Gappers.MinuteBarMath.ToEastern(r.DecisionUtc));
        e.DecisionUtc = r.DecisionUtc;
        e.Split = LiveSplit;
        e.FeatureVersion = r.FeatureVersion;
        e.IsCandidate = r.Reason != FormationSnapshotRow.ControlReason && (r.Reason == FormationSnapshotRow.GatedReason || r.TriggerPassed);
        e.IsControl = r.Reason == FormationSnapshotRow.ControlReason;
        e.GapPct = r.GapPct;
        e.PmCumVol = r.PmCumVol;
        e.VolRatioTod = r.VolRatioTod;
        e.OwnProfileRatio = r.OwnProfileRatio;
        e.Velocity1m = r.Velocity1m;
        e.Velocity3m = r.Velocity3m;
        e.Velocity5m = r.Velocity5m;
        e.Velocity15m = r.Velocity15m;
        e.Acceleration = r.Acceleration;
        e.VolumeRoc = r.VolumeRoc;
        e.TradesProxy = r.TradesProxy;
        e.SpreadProxy = r.SpreadProxy;
        e.DistToPmHighPct = r.DistToPmHighPct;
        e.MinsSinceFirstAbnormal = r.MinsSinceFirstAbnormal;
        e.SessionVwap = r.SessionVwap;
        e.AbnormalReturnD1 = r.AbnormalReturnD1;
        e.AbnormalReturnD2 = r.AbnormalReturnD2;
        e.AbnormalReturnD3 = r.AbnormalReturnD3;
        e.AbnormalReturnD4 = r.AbnormalReturnD4;
        e.AbnormalReturnD5 = r.AbnormalReturnD5;
        e.VolumeRatioD1 = r.VolumeRatioD1;
        e.VolumeRatioD2 = r.VolumeRatioD2;
        e.VolumeRatioD3 = r.VolumeRatioD3;
        e.VolumeRatioD4 = r.VolumeRatioD4;
        e.VolumeRatioD5 = r.VolumeRatioD5;
        e.DaysSinceReverseSplit = r.DaysSinceReverseSplit;
        e.DaysSince424B = r.DaysSince424B;
        e.DaysSinceS1 = r.DaysSinceS1;
        e.CatalystScore = r.CatalystScore;
        e.CatalystAgeMinutes = r.CatalystAgeMinutes;
        e.CatalystDirection = r.CatalystDirection;
        e.CatalystPreClass = r.CatalystPreClass;
        e.CatalystStampIsUpperBound = r.CatalystStampIsUpperBound;
        e.Halted = r.Halted;
        e.ExtraJson = PackExtra(r);
        e.ComputedUtc = r.WrittenUtc;
        return e;
    }

    internal static FormationSnapshotRow ToRow(GapperFormationSnapshotRow e)
    {
        var (extra, reason, passed, armed, written) = UnpackExtra(e.ExtraJson);
        return new FormationSnapshotRow
        {
            Source = e.Source,
            Symbol = e.Symbol,
            DateEt = e.DateEt,
            DecisionTimeEt = e.DecisionLabel,
            DecisionUtc = DateTime.SpecifyKind(e.DecisionUtc, DateTimeKind.Utc),
            Reason = reason ?? (e.IsControl ? FormationSnapshotRow.ControlReason : FormationSnapshotRow.ScheduleReason),
            GapPct = e.GapPct,
            PmCumVol = e.PmCumVol,
            VolRatioTod = e.VolRatioTod,
            OwnProfileRatio = e.OwnProfileRatio,
            Velocity1m = e.Velocity1m,
            Velocity3m = e.Velocity3m,
            Velocity5m = e.Velocity5m,
            Velocity15m = e.Velocity15m,
            Acceleration = e.Acceleration,
            VolumeRoc = e.VolumeRoc,
            TradesProxy = e.TradesProxy,
            SpreadProxy = e.SpreadProxy,
            DistToPmHighPct = e.DistToPmHighPct,
            MinsSinceFirstAbnormal = e.MinsSinceFirstAbnormal,
            SessionVwap = e.SessionVwap,
            AbnormalReturnD1 = e.AbnormalReturnD1,
            AbnormalReturnD2 = e.AbnormalReturnD2,
            AbnormalReturnD3 = e.AbnormalReturnD3,
            AbnormalReturnD4 = e.AbnormalReturnD4,
            AbnormalReturnD5 = e.AbnormalReturnD5,
            VolumeRatioD1 = e.VolumeRatioD1,
            VolumeRatioD2 = e.VolumeRatioD2,
            VolumeRatioD3 = e.VolumeRatioD3,
            VolumeRatioD4 = e.VolumeRatioD4,
            VolumeRatioD5 = e.VolumeRatioD5,
            DaysSinceReverseSplit = e.DaysSinceReverseSplit,
            DaysSince424B = e.DaysSince424B,
            DaysSinceS1 = e.DaysSinceS1,
            CatalystScore = e.CatalystScore,
            CatalystAgeMinutes = e.CatalystAgeMinutes,
            CatalystDirection = e.CatalystDirection,
            CatalystPreClass = e.CatalystPreClass,
            CatalystStampIsUpperBound = e.CatalystStampIsUpperBound,
            Halted = e.Halted,
            ExtraJson = extra,
            FeatureVersion = e.FeatureVersion,
            TriggerPassed = passed,
            ArmedStrategyId = armed,
            WrittenUtc = written ?? DateTime.SpecifyKind(e.ComputedUtc, DateTimeKind.Utc),
        };
    }

    /// <summary>The snapshot's Extra object plus the live-only fields the table has no columns for.</summary>
    internal static string PackExtra(FormationSnapshotRow r)
    {
        JsonObject obj;
        try { obj = JsonNode.Parse(string.IsNullOrWhiteSpace(r.ExtraJson) ? "{}" : r.ExtraJson) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { obj = new JsonObject(); }
        obj[ReasonKey] = r.Reason;
        obj[TriggerPassedKey] = r.TriggerPassed;
        obj[ArmedStrategyIdKey] = r.ArmedStrategyId?.ToString();
        obj[WrittenUtcKey] = r.WrittenUtc.ToString("O", CultureInfo.InvariantCulture);
        return obj.ToJsonString();
    }

    internal static (string Extra, string? Reason, bool TriggerPassed, Guid? ArmedStrategyId, DateTime? WrittenUtc) UnpackExtra(string? json)
    {
        JsonObject? obj = null;
        try { obj = string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { }
        if (obj is null) return ("{}", null, false, null, null);
        var reason = obj[ReasonKey]?.GetValue<string>();
        var passed = obj[TriggerPassedKey] is JsonValue p && p.TryGetValue<bool>(out var b) && b;
        Guid? armed = Guid.TryParse(obj[ArmedStrategyIdKey]?.GetValue<string>(), out var g) ? g : null;
        DateTime? written = DateTime.TryParse(obj[WrittenUtcKey]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var w) ? w : null;
        foreach (var key in new[] { ReasonKey, TriggerPassedKey, ArmedStrategyIdKey, WrittenUtcKey }) obj.Remove(key);
        return (obj.ToJsonString(), reason, passed, armed, written);
    }
}
