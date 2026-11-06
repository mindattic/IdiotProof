using IdiotProof.DataFeeds.Streaming;

namespace IdiotProof.Core.Catalysts;

/// <summary>A point-in-time copy of <see cref="CatalystIntakeState"/> for <c>catalyst intake-status</c>. Times are UTC; render them in ET.</summary>
public sealed record CatalystIntakeSnapshot
{
    public bool Enabled { get; init; }
    public Guid? OwnerUserId { get; init; }
    public bool Leader { get; init; }
    public bool Running { get; init; }
    public DateTime? StartedUtc { get; init; }
    public DateTime? StoppedUtc { get; init; }
    public AlpacaStreamHealth? Stream { get; init; }
    public DateTime? LastMessageUtc { get; init; }
    public long NewsMessages { get; init; }
    public long NewsInserted { get; init; }
    public long NewsDuplicates { get; init; }
    public long NewsNoSymbol { get; init; }
    public bool EdgarPolling { get; init; }
    public DateTime? EdgarLastPollUtc { get; init; }
    public EdgarPollSummary? EdgarLastPoll { get; init; }
    public long EdgarInserted { get; init; }
    public bool LlmClassify { get; init; }
    public string? LastError { get; init; }
    public DateTime? LastErrorUtc { get; init; }
}

/// <summary>What the intake worker is doing right now, in memory, for <c>catalyst intake-status</c>.</summary>
public sealed class CatalystIntakeState
{
    private readonly Lock gate = new();
    private CatalystIntakeConfig config = new(false, null, false, false);
    private bool leader;
    private bool running;
    private DateTime? startedUtc;
    private DateTime? stoppedUtc;
    private AlpacaStreamSession? session;
    private DateTime? lastMessageUtc;
    private long newsMessages, newsInserted, newsDuplicates, newsNoSymbol, edgarInserted;
    private DateTime? edgarLastPollUtc;
    private EdgarPollSummary? edgarLastPoll;
    private string? lastError;
    private DateTime? lastErrorUtc;

    public void SetConfig(CatalystIntakeConfig c, bool isLeader)
    {
        lock (gate) { config = c; leader = isLeader; }
    }

    public void MarkStarted(AlpacaStreamSession s, DateTime nowUtc)
    {
        lock (gate) { running = true; startedUtc = nowUtc; stoppedUtc = null; session = s; }
    }

    public void MarkStopped(DateTime nowUtc)
    {
        lock (gate) { running = false; stoppedUtc = nowUtc; session = null; }
    }

    public void CountNews(CatalystIngestOutcome outcome, DateTime nowUtc)
    {
        lock (gate)
        {
            newsMessages++;
            lastMessageUtc = nowUtc;
            switch (outcome)
            {
                case CatalystIngestOutcome.Inserted: newsInserted++; break;
                case CatalystIngestOutcome.Duplicate: newsDuplicates++; break;
                default: newsNoSymbol++; break;
            }
        }
    }

    public void RecordEdgarPoll(EdgarPollSummary summary, DateTime nowUtc)
    {
        lock (gate) { edgarLastPoll = summary; edgarLastPollUtc = nowUtc; edgarInserted += summary.Inserted; }
    }

    public void RecordError(string error, DateTime nowUtc)
    {
        lock (gate) { lastError = error; lastErrorUtc = nowUtc; }
    }

    public CatalystIntakeSnapshot Snapshot()
    {
        lock (gate)
        {
            return new CatalystIntakeSnapshot
            {
                Enabled = config.Enabled,
                OwnerUserId = config.OwnerUserId,
                Leader = leader,
                Running = running,
                StartedUtc = startedUtc,
                StoppedUtc = stoppedUtc,
                Stream = session?.Health(1, $"{newsMessages} news item(s)"),
                LastMessageUtc = lastMessageUtc,
                NewsMessages = newsMessages,
                NewsInserted = newsInserted,
                NewsDuplicates = newsDuplicates,
                NewsNoSymbol = newsNoSymbol,
                EdgarPolling = config.EdgarPolling,
                EdgarLastPollUtc = edgarLastPollUtc,
                EdgarLastPoll = edgarLastPoll,
                EdgarInserted = edgarInserted,
                LlmClassify = config.LlmClassify,
                LastError = lastError,
                LastErrorUtc = lastErrorUtc,
            };
        }
    }
}
