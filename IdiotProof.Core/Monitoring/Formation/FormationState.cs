namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>A point-in-time copy of <see cref="FormationState"/> for <c>gapper formation status</c>. Times are UTC; render them in ET.</summary>
public sealed record FormationStatusSnapshot
{
    public FormationConfig Config { get; init; } = new();
    public bool Leader { get; init; }
    public bool Running { get; init; }
    public DateTime? StartedUtc { get; init; }
    public DateTime? StoppedUtc { get; init; }
    public string? StopReason { get; init; }
    public DateOnly? DayEt { get; init; }
    public IReadOnlyList<FormationMember> Universe { get; init; } = [];
    public DateTime? UniverseBuiltUtc { get; init; }
    public string? UniverseNote { get; init; }
    public DateTime? LastTickUtc { get; init; }
    public long Ticks { get; init; }
    public long RowsToday { get; init; }
    public long RowsTotal { get; init; }
    public int TriggeredToday { get; init; }
    public IReadOnlyList<string> HaltedSymbols { get; init; } = [];
    public string? Stream { get; init; }
    public bool ProfileLoaded { get; init; }
    public string? LastError { get; init; }
    public DateTime? LastErrorUtc { get; init; }
    public DateOnly? LastPruneDayEt { get; init; }
    public DateOnly? ScheduledAutoLastRunEt { get; init; }
}

/// <summary>
/// What the formation worker is doing right now, in memory: the day's universe, which
/// (symbol, decision time) pairs are already written so a 15-second tick never writes a row twice,
/// which symbols are halted per the SIP status channel, and the counters the status operation shows.
/// </summary>
public sealed class FormationState
{
    private readonly Lock gate = new();
    private FormationConfig config = new();
    private bool leader;
    private bool running;
    private DateTime? startedUtc, stoppedUtc, lastTickUtc, lastErrorUtc, universeBuiltUtc;
    private string? stopReason, universeNote, lastError, stream;
    private DateOnly? dayEt, lastPruneDayEt, scheduledAutoLastRunEt;
    private IReadOnlyList<FormationMember> universe = [];
    private long ticks, rowsToday, rowsTotal;
    private bool profileLoaded;
    private readonly HashSet<(string Symbol, string TimeEt)> decided = new();
    private readonly HashSet<string> triggered = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (bool Halted, DateTime AtUtc)> halts = new(StringComparer.OrdinalIgnoreCase);

    public void SetConfig(FormationConfig c, bool isLeader)
    {
        lock (gate) { config = c; leader = isLeader; }
    }

    public void MarkStarted(DateTime nowUtc)
    {
        lock (gate) { running = true; startedUtc = nowUtc; stoppedUtc = null; stopReason = null; }
    }

    public void MarkStopped(DateTime nowUtc, string why)
    {
        lock (gate) { running = false; stoppedUtc = nowUtc; stopReason = why; stream = null; }
    }

    public void SetStream(string? status)
    {
        lock (gate) stream = status;
    }

    public void SetProfileLoaded(bool loaded)
    {
        lock (gate) profileLoaded = loaded;
    }

    /// <summary>Starts a new ET day: forgets decided pairs, triggers, halts and the day's row count.</summary>
    public bool EnsureDay(DateOnly day)
    {
        lock (gate)
        {
            if (dayEt == day) return false;
            dayEt = day;
            decided.Clear();
            triggered.Clear();
            halts.Clear();
            rowsToday = 0;
            universe = [];
            universeBuiltUtc = null;
            universeNote = null;
            return true;
        }
    }

    public DateOnly? DayEt
    {
        get { lock (gate) return dayEt; }
    }

    public void SetUniverse(IReadOnlyList<FormationMember> members, DateTime nowUtc, string? note)
    {
        lock (gate) { universe = members; universeBuiltUtc = nowUtc; universeNote = note; }
    }

    public IReadOnlyList<FormationMember> Universe
    {
        get { lock (gate) return universe; }
    }

    public DateTime? UniverseBuiltUtc
    {
        get { lock (gate) return universeBuiltUtc; }
    }

    /// <summary>True the first time a (symbol, decision time) pair is claimed today; false when it was already decided.</summary>
    public bool TryDecide(string symbol, string timeEt)
    {
        lock (gate) return decided.Add((symbol.ToUpperInvariant(), timeEt));
    }

    public bool IsDecided(string symbol, string timeEt)
    {
        lock (gate) return decided.Contains((symbol.ToUpperInvariant(), timeEt));
    }

    /// <summary>True the first time a symbol passes the trigger today.</summary>
    public bool TryMarkTriggered(string symbol)
    {
        lock (gate) return triggered.Add(symbol);
    }

    public void SetHalted(string symbol, bool halted, DateTime atUtc)
    {
        lock (gate) halts[symbol] = (halted, atUtc);
    }

    /// <summary>The symbol's halt state as last reported; null when no status message has named it today.</summary>
    public bool? Halted(string symbol)
    {
        lock (gate) return halts.TryGetValue(symbol, out var h) ? h.Halted : null;
    }

    public void RecordTick(DateTime nowUtc, int rowsWritten)
    {
        lock (gate) { ticks++; lastTickUtc = nowUtc; rowsToday += rowsWritten; rowsTotal += rowsWritten; }
    }

    public void RecordError(string error, DateTime nowUtc)
    {
        lock (gate) { lastError = error; lastErrorUtc = nowUtc; }
    }

    public void MarkPruned(DateOnly day)
    {
        lock (gate) lastPruneDayEt = day;
    }

    public DateOnly? LastPruneDayEt
    {
        get { lock (gate) return lastPruneDayEt; }
    }

    public void MarkScheduledAutoRun(DateOnly day)
    {
        lock (gate) scheduledAutoLastRunEt = day;
    }

    public DateOnly? ScheduledAutoLastRunEt
    {
        get { lock (gate) return scheduledAutoLastRunEt; }
    }

    public FormationStatusSnapshot Snapshot()
    {
        lock (gate)
        {
            return new FormationStatusSnapshot
            {
                Config = config,
                Leader = leader,
                Running = running,
                StartedUtc = startedUtc,
                StoppedUtc = stoppedUtc,
                StopReason = stopReason,
                DayEt = dayEt,
                Universe = universe,
                UniverseBuiltUtc = universeBuiltUtc,
                UniverseNote = universeNote,
                LastTickUtc = lastTickUtc,
                Ticks = ticks,
                RowsToday = rowsToday,
                RowsTotal = rowsTotal,
                TriggeredToday = triggered.Count,
                HaltedSymbols = halts.Where(h => h.Value.Halted).Select(h => h.Key).OrderBy(s => s, StringComparer.Ordinal).ToList(),
                Stream = stream,
                ProfileLoaded = profileLoaded,
                LastError = lastError,
                LastErrorUtc = lastErrorUtc,
                LastPruneDayEt = lastPruneDayEt,
                ScheduledAutoLastRunEt = scheduledAutoLastRunEt,
            };
        }
    }
}
