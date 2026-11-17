using System.Data;
using System.Text.Json;
using IdiotProof.Core.History;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>A symbol's reference facts for one session: its latest reverse split and its filings, point-in-time trimmed by the feature compute.</summary>
public sealed record FormationReference(DateOnly? ReverseSplit, IReadOnlyList<FilingObservation> Filings);

/// <summary>
/// The reverse-split and filing inputs the gapper study's snapshots carried (DaysSinceReverseSplit, DaysSince424B,
/// DaysSinceS1), read for the live recorder from the permanent history (IP-T454 keeps splits and every EDGAR filing),
/// with the study's own rules (GapperSnapshotStage): a reverse split is a split whose old rate exceeds its new rate
/// within <see cref="DaysSinceMaxDays"/> days, and a filing without an acceptance time counts as accepted 23:59:59 ET
/// on its filing date. Without these the detector (IP-T447) would score those three inputs as the training mean every
/// time. Read-only; a history database that does not answer yields nothing, and the snapshot carries nulls as before.
/// </summary>
public sealed class FormationReferenceSource(HistoryDatabase history, ILogger<FormationReferenceSource> log, TimeProvider? time = null)
{
    /// <summary>A session's facts change only when a filing lands, so one read serves the recorder's ticks for this long.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private readonly Lock gate = new();
    private (DateOnly Date, string Key, DateTime AtUtc, Dictionary<string, FormationReference> Value)? cache;

    /// <summary><see cref="GetAsync"/> cached per session and symbol set for <see cref="CacheFor"/>.</summary>
    public async Task<Dictionary<string, FormationReference>> GetCachedAsync(IReadOnlyCollection<string> symbols, DateOnly dateEt, CancellationToken ct)
    {
        var key = string.Join(",", symbols.Select(s => s.ToUpperInvariant()).Order(StringComparer.Ordinal));
        var now = (time ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        lock (gate)
            if (cache is { } c && c.Date == dateEt && c.Key == key && now - c.AtUtc < CacheFor) return c.Value;
        var value = await GetAsync(symbols, dateEt, ct).ConfigureAwait(false);
        lock (gate) cache = (dateEt, key, now, value);
        return value;
    }

    /// <summary>Mirror of the study's params.json features.daysSinceMaxDays (365).</summary>
    public const int DaysSinceMaxDays = 365;

    public async Task<Dictionary<string, FormationReference>> GetAsync(IReadOnlyCollection<string> symbols, DateOnly dateEt, CancellationToken ct)
    {
        var result = new Dictionary<string, FormationReference>(StringComparer.OrdinalIgnoreCase);
        var syms = symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length is > 0 and <= 16).Distinct().ToList();
        if (syms.Count == 0) return result;
        try
        {
            await using var c = await history.OpenAsync(ct).ConfigureAwait(false);
            var reverse = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
            await using (var q = new SqlCommand("""
                SELECT Symbol, MAX(ExDate) FROM dbo.Splits
                WHERE OldRate > NewRate AND ExDate <= @d AND ExDate >= @from AND Symbol IN (SELECT value FROM STRING_SPLIT(@syms, ','))
                GROUP BY Symbol
                """, c))
            {
                q.Parameters.Add("@d", SqlDbType.Date).Value = dateEt.ToDateTime(TimeOnly.MinValue);
                q.Parameters.Add("@from", SqlDbType.Date).Value = dateEt.AddDays(-DaysSinceMaxDays).ToDateTime(TimeOnly.MinValue);
                q.Parameters.Add("@syms", SqlDbType.NVarChar, -1).Value = string.Join(",", syms);
                await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await r.ReadAsync(ct).ConfigureAwait(false)) reverse[r.GetString(0)] = DateOnly.FromDateTime(r.GetDateTime(1));
            }
            var filings = new Dictionary<string, List<FilingObservation>>(StringComparer.OrdinalIgnoreCase);
            await using (var q = new SqlCommand("""
                SELECT Symbol, FormType, FiledDate, AcceptedUtc, ItemsJson FROM dbo.Filings
                WHERE FiledDate >= @from AND FiledDate <= @d AND (FormType LIKE '424B%' OR FormType LIKE 'S-1%')
                  AND Symbol IN (SELECT value FROM STRING_SPLIT(@syms, ','))
                """, c))
            {
                q.Parameters.Add("@d", SqlDbType.Date).Value = dateEt.ToDateTime(TimeOnly.MinValue);
                q.Parameters.Add("@from", SqlDbType.Date).Value = dateEt.AddDays(-DaysSinceMaxDays - 5).ToDateTime(TimeOnly.MinValue);
                q.Parameters.Add("@syms", SqlDbType.NVarChar, -1).Value = string.Join(",", syms);
                await using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    var filed = DateOnly.FromDateTime(r.GetDateTime(2));
                    var accepted = r.IsDBNull(3) ? EasternTime.ToUtc(filed.ToDateTime(new TimeOnly(23, 59, 59))) : DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc);
                    var items = r.IsDBNull(4) ? [] : JsonSerializer.Deserialize<List<string>>(r.GetString(4)) ?? [];
                    if (!filings.TryGetValue(r.GetString(0), out var list)) filings[r.GetString(0)] = list = [];
                    list.Add(new FilingObservation(r.GetString(1), accepted, items));
                }
            }
            foreach (var s in syms)
                if (reverse.ContainsKey(s) || filings.ContainsKey(s))
                    result[s] = new(reverse.TryGetValue(s, out var rs) ? rs : null, filings.TryGetValue(s, out var f) ? f : []);
        }
        catch (SqlException ex)
        {
            log.LogWarning("Formation: history database unavailable for splits and filings ({Message}); snapshots carry no reverse-split or filing ages.", ex.Message);
        }
        return result;
    }
}
