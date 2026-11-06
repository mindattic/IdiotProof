namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>Filter for reading snapshot rows back; every field optional, newest rows first.</summary>
public sealed record GapperSnapshotQuery(string? Symbol = null, DateOnly? DateEt = null, int Limit = 200);

/// <summary>
/// Where the formation recorder puts its rows. The default is the in-memory ring buffer below;
/// the SQL-backed sink over the GapperFormationSnapshots table replaces it once that table exists,
/// without the worker, the scanner or the operations changing.
/// </summary>
public interface IGapperSnapshotSink
{
    Task WriteAsync(IReadOnlyList<FormationSnapshotRow> rows, CancellationToken ct = default);

    Task<IReadOnlyList<FormationSnapshotRow>> ReadAsync(GapperSnapshotQuery query, CancellationToken ct = default);

    /// <summary>Deletes rows whose decision instant is before <paramref name="olderThanUtc"/>; returns how many went.</summary>
    Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken ct = default);
}

/// <summary>A bounded ring of the most recent rows: the default sink, the test sink, and what the status operation reads until the SQL sink lands.</summary>
public sealed class InMemoryGapperSnapshotSink(int capacity = InMemoryGapperSnapshotSink.DefaultCapacity) : IGapperSnapshotSink
{
    public const int DefaultCapacity = 20_000;

    private readonly Lock gate = new();
    private readonly Queue<FormationSnapshotRow> rows = new();
    private readonly int capacity = Math.Max(1, capacity);

    public int Count
    {
        get { lock (gate) return rows.Count; }
    }

    public long TotalWritten { get; private set; }

    public Task WriteAsync(IReadOnlyList<FormationSnapshotRow> batch, CancellationToken ct = default)
    {
        lock (gate)
        {
            foreach (var r in batch)
            {
                rows.Enqueue(r);
                TotalWritten++;
                while (rows.Count > capacity) rows.Dequeue();
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FormationSnapshotRow>> ReadAsync(GapperSnapshotQuery query, CancellationToken ct = default)
    {
        List<FormationSnapshotRow> all;
        lock (gate) all = [.. rows];
        IEnumerable<FormationSnapshotRow> q = all;
        if (!string.IsNullOrWhiteSpace(query.Symbol))
            q = q.Where(r => r.Symbol.Equals(query.Symbol.Trim(), StringComparison.OrdinalIgnoreCase));
        if (query.DateEt is { } d) q = q.Where(r => r.DateEt == d);
        IReadOnlyList<FormationSnapshotRow> result = q
            .OrderByDescending(r => r.DecisionUtc).ThenBy(r => r.Symbol, StringComparer.Ordinal)
            .Take(Math.Max(1, query.Limit)).ToList();
        return Task.FromResult(result);
    }

    public Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken ct = default)
    {
        lock (gate)
        {
            var keep = rows.Where(r => r.DecisionUtc >= olderThanUtc).ToList();
            var removed = rows.Count - keep.Count;
            rows.Clear();
            foreach (var r in keep) rows.Enqueue(r);
            return Task.FromResult(removed);
        }
    }
}
