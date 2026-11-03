using System.Collections.Concurrent;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Core.Monitoring;

/// <summary>
/// The Monitor's in-memory record of what it last saw per strategy, so cross triggers
/// (BreaksAbove/BreaksBelow, OnReclaim, OnVwapReclaim/OnVwapLoss) still fire when the cross
/// happened inside bars the Monitor never evaluated: a DB outage, failure backoff or a restart
/// (IP-T361). Kept in memory on purpose: it has to work exactly when SQL doesn't.
///
/// While ticks are continuous (the last look is the prior bar or newer) nothing changes and the
/// prior-bar semantics stand. After a gap, the pre-gap look is offered as an extra "other side"
/// for the whole first bar back, mirroring how a prior-bar cross stays valid through its bar.
/// A look from another ET day, or older than <see cref="MaxGap"/>, is ignored: a cross that far
/// back is not a trigger any more.
/// </summary>
public sealed class CrossTriggerMemory
{
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(30);

    private sealed record Look(DateTime BarEndUtc, DateTime ObservedUtc, LastLookObservation Observation);

    private sealed record Entry(Look Last, Look? GapRef, DateTime? GapBarEndUtc);

    private readonly ConcurrentDictionary<Guid, Entry> entries = new();

    /// <summary>
    /// Sets <see cref="IndicatorSnapshot.LastLook"/> when the Monitor missed at least one bar since
    /// its last look at <paramref name="strategyId"/>. Call before evaluating the conditions.
    /// </summary>
    public void Apply(Guid strategyId, IndicatorSnapshot snapshot, DateTime? priorBarEndUtc, DateTime nowUtc)
    {
        snapshot.LastLook = null;
        if (!entries.TryGetValue(strategyId, out var entry)) return;

        if (entry.GapRef is { } gap && entry.GapBarEndUtc == snapshot.Timestamp)
        {
            if (IsUsable(gap, snapshot, nowUtc)) snapshot.LastLook = gap.Observation;
            return;
        }

        if (priorBarEndUtc is { } prior && entry.Last.BarEndUtc < prior && IsUsable(entry.Last, snapshot, nowUtc))
            snapshot.LastLook = entry.Last.Observation;
    }

    /// <summary>Remembers this evaluation as the newest look. Call after evaluating the conditions.</summary>
    public void Record(Guid strategyId, IndicatorSnapshot snapshot, DateTime nowUtc)
    {
        var look = new Look(snapshot.Timestamp, nowUtc,
            new LastLookObservation(snapshot.Price, snapshot.Vwap, new Dictionary<int, double>(snapshot.Emas)));

        entries.AddOrUpdate(strategyId,
            _ => new Entry(look, null, null),
            (_, old) =>
            {
                if (snapshot.LastLook is null) return new Entry(look, null, null);
                var gapRef = old.GapRef is { } g && old.GapBarEndUtc == snapshot.Timestamp ? g : old.Last;
                return new Entry(look, gapRef, snapshot.Timestamp);
            });
    }

    /// <summary>Drops a strategy's looks (it entered a position, or is no longer evaluated).</summary>
    public void Forget(Guid strategyId) => entries.TryRemove(strategyId, out _);

    private static bool IsUsable(Look look, IndicatorSnapshot snapshot, DateTime nowUtc) =>
        nowUtc - look.ObservedUtc <= MaxGap && EtDay(look.BarEndUtc) == EtDay(snapshot.Timestamp);

    private static DateOnly EtDay(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTime.Eastern));
}
