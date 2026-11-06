using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>Once per ET day, drops snapshot rows older than <c>formation.retentionDays</c> through the sink.</summary>
public static class FormationRetention
{
    public static async Task<int> PruneIfDueAsync(IGapperSnapshotSink sink, FormationState state, FormationConfig cfg, DateTime nowUtc, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(MinuteBarMath.ToEastern(nowUtc));
        if (state.LastPruneDayEt == today) return 0;
        state.MarkPruned(today);
        return await sink.PruneAsync(nowUtc.AddDays(-cfg.RetentionDays), ct);
    }
}
