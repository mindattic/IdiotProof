namespace IdiotProof.Core.Catalysts;

/// <summary>What one IP-T602 source did in this process, for <c>catalyst sources</c>. Times are UTC; render them in ET.</summary>
public sealed record CatalystSourceSnapshot
{
    public string Name { get; init; } = "";
    public string Status { get; init; } = "not started";
    public bool Running { get; init; }
    public DateTime? LastPollUtc { get; init; }
    public DateTime? LastOkUtc { get; init; }
    public DateTime? NextPollUtc { get; init; }
    public long Polls { get; init; }
    public long NotModified { get; init; }
    public long ItemsSeen { get; init; }
    public long Inserted { get; init; }
    public long Merged { get; init; }
    public long Duplicates { get; init; }
    public long NoSymbol { get; init; }
    public long Signals { get; init; }
    public string? LastSummary { get; init; }
    public string? LastError { get; init; }
    public DateTime? LastErrorUtc { get; init; }
    public int ConsecutiveErrors { get; init; }
}

/// <summary>One poll's outcome, folded into <see cref="CatalystSourceState"/>.</summary>
public sealed record SourcePollSummary(int ItemsSeen, int Inserted, int Merged, int Duplicates, int NoSymbol, int Signals, int NotModified, int Failed, string Summary);

/// <summary>Every IP-T602 source's in-memory status, written by <see cref="CatalystSourcesWorker"/>.</summary>
public sealed class CatalystSourceState
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, CatalystSourceSnapshot> sources = new(StringComparer.OrdinalIgnoreCase);

    public void SetStatus(string name, string status, bool running, DateTime? nextPollUtc = null)
    {
        lock (gate) sources[name] = Get(name) with { Status = status, Running = running, NextPollUtc = nextPollUtc };
    }

    public void RecordPoll(string name, SourcePollSummary s, DateTime nowUtc, DateTime nextPollUtc)
    {
        lock (gate)
        {
            var c = Get(name);
            sources[name] = c with
            {
                LastPollUtc = nowUtc,
                LastOkUtc = nowUtc,
                NextPollUtc = nextPollUtc,
                Polls = c.Polls + 1,
                NotModified = c.NotModified + s.NotModified,
                ItemsSeen = c.ItemsSeen + s.ItemsSeen,
                Inserted = c.Inserted + s.Inserted,
                Merged = c.Merged + s.Merged,
                Duplicates = c.Duplicates + s.Duplicates,
                NoSymbol = c.NoSymbol + s.NoSymbol,
                Signals = c.Signals + s.Signals,
                LastSummary = s.Summary,
                ConsecutiveErrors = 0,
            };
        }
    }

    public void RecordError(string name, string error, DateTime nowUtc, DateTime nextPollUtc)
    {
        lock (gate)
        {
            var c = Get(name);
            sources[name] = c with
            {
                LastPollUtc = nowUtc,
                NextPollUtc = nextPollUtc,
                Polls = c.Polls + 1,
                LastError = error,
                LastErrorUtc = nowUtc,
                ConsecutiveErrors = c.ConsecutiveErrors + 1,
            };
        }
    }

    public int ConsecutiveErrors(string name)
    {
        lock (gate) return Get(name).ConsecutiveErrors;
    }

    public IReadOnlyList<CatalystSourceSnapshot> Snapshot()
    {
        lock (gate) return CatalystSources.All.Select(s => Get(s.Name)).ToList();
    }

    private CatalystSourceSnapshot Get(string name) =>
        sources.TryGetValue(name, out var s) ? s : new CatalystSourceSnapshot { Name = name };
}

/// <summary>Reads a Vault Tokens entry (the same bucket and fallback as the fundamentals vendors); never logs a value.</summary>
public sealed class CatalystSourceKeys(Func<string, string?> get)
{
    public string? Get(string entry) => get(entry);

    /// <summary>The first missing entry of a source, or null when every one it needs is present.</summary>
    public string? Missing(CatalystSourceInfo source) => source.VaultEntries.FirstOrDefault(e => string.IsNullOrWhiteSpace(get(e)));
}
