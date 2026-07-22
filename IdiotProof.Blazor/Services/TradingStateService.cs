using IdiotProof.Models;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Singleton in-memory state for the running trading application.
/// All members are thread-safe.
/// </summary>
public sealed class TradingStateService
{
    private readonly Lock sync = new();
    private readonly List<TradeSignal> recentSignals = [];
    private readonly Dictionary<string, LatestPrice> livePrices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Position>> positions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LlmVotingResult> signalVotes = new(StringComparer.OrdinalIgnoreCase);

    private const int MaxSignals = 500;

    public event Action? OnStateChanged;

    // Engine state — written under lock(sync); getters also lock so readers
    // on other threads observe writes without JIT register-caching stale values.
    private bool isEngineRunning;
    private DateTime? lastEvaluationUtc;
    private int totalSignalsToday;

    public bool IsEngineRunning      { get { lock (sync) return isEngineRunning; } }
    public DateTime? LastEvaluationUtc { get { lock (sync) return lastEvaluationUtc; } }
    public int TotalSignalsToday     { get { lock (sync) return totalSignalsToday; } }

    /// <summary>Returns a snapshot of the most recent signals (newest first).</summary>
    public IReadOnlyList<TradeSignal> RecentSignals
    {
        get
        {
            lock (sync) return recentSignals.ToList();
        }
    }

    /// <summary>Returns a snapshot of current live prices.</summary>
    public IReadOnlyDictionary<string, LatestPrice> LivePrices
    {
        get
        {
            lock (sync) return new Dictionary<string, LatestPrice>(livePrices, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Returns a snapshot of signal voting results keyed by signal key.</summary>
    public IReadOnlyDictionary<string, LlmVotingResult> SignalVotes
    {
        get
        {
            lock (sync) return new Dictionary<string, LlmVotingResult>(signalVotes, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Prepend a new signal to the list (newest first), capped at MaxSignals.</summary>
    public void AddSignal(TradeSignal signal)
    {
        lock (sync)
        {
            recentSignals.Insert(0, signal);
            if (recentSignals.Count > MaxSignals)
                recentSignals.RemoveAt(recentSignals.Count - 1);

            if (signal.GeneratedUtc.Date == DateTime.UtcNow.Date)
                totalSignalsToday++;
        }
        RaiseChanged();
    }

    /// <summary>Update the live price for a symbol.</summary>
    public void UpdatePrice(LatestPrice price)
    {
        lock (sync) livePrices[price.Symbol] = price;
        RaiseChanged();
    }

    /// <summary>Replace all positions for a user+broker combination.</summary>
    public void UpdatePositions(string userId, BrokerType broker, List<Position> newPositions)
    {
        lock (sync) positions[$"{userId}:{broker}"] = newPositions;
        RaiseChanged();
    }

    /// <summary>Returns positions for a specific user, keyed by broker name.</summary>
    public IReadOnlyDictionary<string, List<Position>> GetPositionsForUser(string userId)
    {
        lock (sync)
        {
            var prefix = $"{userId}:";
            return positions
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    kv => kv.Key[prefix.Length..],
                    kv => kv.Value,
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Returns the sum of unrealized P&amp;L across all of a user's positions.</summary>
    public decimal GetTodaysPnlForUser(string userId)
    {
        lock (sync)
        {
            var prefix = $"{userId}:";
            return positions
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .SelectMany(kv => kv.Value)
                .Sum(p => p.UnrealizedPnl);
        }
    }

    /// <summary>Store the LLM voting result for a signal.</summary>
    public void StoreVote(string signalKey, LlmVotingResult result)
    {
        lock (sync) signalVotes[signalKey] = result;
        RaiseChanged();
    }

    /// <summary>Returns recent signals for a specific user.</summary>
    public IReadOnlyList<TradeSignal> GetRecentSignalsForUser(string userId)
    {
        lock (sync)
            return recentSignals.Where(s => s.UserId == userId).ToList();
    }

    /// <summary>Returns recent signals filtered to a specific symbol for a user.</summary>
    public IReadOnlyList<TradeSignal> GetSignalsFor(string symbol, string? userId = null)
    {
        lock (sync)
            return recentSignals.Where(s =>
                s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) &&
                (userId == null || s.UserId == userId)).ToList();
    }

    /// <summary>Mark the engine as running or stopped.</summary>
    public void SetEngineRunning(bool running)
    {
        lock (sync) isEngineRunning = running;
        RaiseChanged();
    }

    /// <summary>Record that an evaluation cycle completed.</summary>
    public void RecordEvaluation()
    {
        lock (sync) lastEvaluationUtc = DateTime.UtcNow;
        RaiseChanged();
    }

    private void RaiseChanged() => OnStateChanged?.Invoke();
}
