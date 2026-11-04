using IdiotProof.Evidence.Information;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Economics;
using IdiotProof.Evidence.Leaders;
using IdiotProof.Models;

namespace IdiotProof.Operations.Evidence;

public sealed record WalkForwardSettings
{
    public int Window { get; init; } = 20;
    public int Horizon { get; init; } = 5;
    public int TrainDays { get; init; } = 20;
    public int MinTrainDays { get; init; } = 3;
    public int K { get; init; } = 25;
    public double MinSimilarity { get; init; } = 0.8;
    public int MinNeighbors { get; init; } = 8;
    public double MinAgreement { get; init; } = 0.6;
    public double DeadBand { get; init; }
    public double RoundTripCost { get; init; } = 0.003;
    /// <summary>
    /// When true, each (symbol, day) gets its own round-trip cost from <see cref="TradingCosts.Estimate"/>
    /// over the prior sessions only, instead of the flat <see cref="RoundTripCost"/>.
    /// </summary>
    public bool ModelCosts { get; init; }
    public CostParameters Costs { get; init; } = new();
    /// <summary>Shares per call for the impact estimate (1 matches the default order-size policy).</summary>
    public double Shares { get; init; } = 1;
    public bool IncludeVolume { get; init; }
    public EvidenceSession Session { get; init; } = EvidenceSession.Rth;
    public int Seed { get; init; } = 1337;
    /// <summary>Where a call fills: the next bar's open (default) or, as a bid-ask-bounce diagnostic, the signal bar's close.</summary>
    public EntryTiming Entry { get; init; } = EntryTiming.NextOpen;
    public EvidenceModel Model { get; init; } = EvidenceModel.Analog;
    /// <summary>Leader model: the symbols each target's leaders are discovered among (besides nothing else).</summary>
    public IReadOnlyList<string> LeaderCandidates { get; init; } = ["SPY", "QQQ", "IWM"];
    public int LeaderLookback { get; init; } = 5;
    public int LeaderMaxHops { get; init; } = 2;
    public double LeaderMinInfluence { get; init; } = 0.05;
    public EdgeMeasure LeaderEdge { get; init; } = EdgeMeasure.Correlation;
    /// <summary>Leader model: keep only lag-1 correlation edges above the random-matrix noise floor.</summary>
    public bool LeaderCleanRmt { get; init; }
}

/// <summary>One evaluated (symbol, day): what the model trained on and how its calls scored.</summary>
public sealed record WalkForwardFold(string Symbol, DateOnly Day, DateOnly FirstTrainingDay, DateOnly LastTrainingDay,
    DateTime LastTrainingBarUtc, DateTime FirstTestBarUtc, int TrainingWindows, int Calls, int Hits, int Abstentions);

public sealed record WalkForwardSymbolScore(string Symbol, int Days, int Calls, int Hits, double HitRate, double PValue, double MeanEdge);

public sealed record WalkForwardReport(
    DirectionalScorecard Overall,
    IReadOnlyList<WalkForwardSymbolScore> Symbols,
    IReadOnlyList<WalkForwardFold> Folds,
    int EvaluationPoints,
    int UpOutcomes,
    int DownOutcomes,
    int SkippedSymbolDays,
    FeedMix? Feeds = null)
{
    /// <summary>Calls and hits pooled per ET day across symbols: the unit for clustered significance.</summary>
    public IReadOnlyList<DayTally> DayTallies() => Folds
        .GroupBy(f => f.Day)
        .OrderBy(g => g.Key)
        .Select(g => new DayTally(g.Key, g.Sum(f => f.Calls), g.Sum(f => f.Hits)))
        .ToList();
}

public sealed record WalkForwardMonth(string Month, int Days, int Calls, int Hits, double HitRate);

/// <summary>
/// Walk-forward evaluation of the LSH pattern-analog model. For every (symbol, day D) it trains a
/// fresh predictor only on that symbol's bars from sessions strictly before D (so every training
/// label resolves before D's first bar, an embargo of at least the horizon), then calls direction on
/// D's bars every <c>horizon</c> bars so evaluated outcomes never overlap. Nothing from D or later
/// is ever indexed.
/// </summary>
public sealed class EvidenceWalkForward(IHistoricalBarStore store)
{
    private const int FetchChunk = 8;

    /// <param name="plan">Symbol → the ET days to evaluate it on.</param>
    public async Task<WalkForwardReport> RunAsync(IReadOnlyDictionary<string, SortedSet<DateOnly>> plan, WalkForwardSettings s, CancellationToken ct)
    {
        if (s.Window < 3 || s.Horizon < 1 || s.TrainDays < 1 || s.MinTrainDays < 1) throw new ArgumentOutOfRangeException(nameof(s));
        var overall = new DirectionalScorecard();
        var perSymbol = new List<WalkForwardSymbolScore>();
        var folds = new List<WalkForwardFold>();
        int points = 0, ups = 0, downs = 0, skipped = 0;
        var sources = new Dictionary<string, int>(StringComparer.Ordinal);
        void Tally(IEnumerable<Candle> list)
        {
            foreach (var (k, v) in BarProvenance.Mix(list).Bars) sources[k] = sources.GetValueOrDefault(k) + v;
        }

        foreach (var chunk in plan.Where(p => p.Value.Count > 0).OrderBy(p => p.Key, StringComparer.Ordinal).Chunk(FetchChunk))
        {
            ct.ThrowIfCancellationRequested();
            var from = chunk.Min(p => p.Value.Min).AddDays(-(s.TrainDays * 3 / 2 + 7));
            var to = chunk.Max(p => p.Value.Max);
            var bars = await store.GetBarsForSymbolsAsync(chunk.Select(p => p.Key), from, to, BarTimeframe.Minute1, ct: ct);
            foreach (var list in bars.Values) Tally(list);
            var leaderPool = s.Model == EvidenceModel.Leaders
                ? (await store.GetBarsForSymbolsAsync(s.LeaderCandidates, from, to, BarTimeframe.Minute1, ct: ct))
                    .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Candle>)EvidenceSupport.Filter(kv.Value, EvidenceTimeframe.Minute, s.Session).ToList(), StringComparer.Ordinal)
                : null;
            if (leaderPool is not null) foreach (var list in leaderPool.Values) Tally(list);

            foreach (var (symbol, days) in chunk)
            {
                var series = bars.TryGetValue(symbol, out var raw) ? EvidenceSupport.Filter(raw, EvidenceTimeframe.Minute, s.Session) : Array.Empty<Candle>();
                var byDay = series.GroupBy(EvidenceSupport.DateEt).ToDictionary(g => g.Key, g => g.ToList());
                var sessions = byDay.Keys.OrderBy(d => d).ToList();
                var card = new DirectionalScorecard();
                int evaluatedDays = 0;

                foreach (var day in days)
                {
                    if (!byDay.TryGetValue(day, out var dayBars)) { skipped++; continue; }
                    var prior = sessions.Where(d => d < day).TakeLast(s.TrainDays).ToList();
                    if (prior.Count < s.MinTrainDays) { skipped++; continue; }

                    if (leaderPool is not null)
                    {
                        var fold = EvaluateLeaders(symbol, day, prior, byDay, dayBars, leaderPool, s, ref points, ref ups, ref downs);
                        if (fold is null) { skipped++; continue; }
                        folds.Add(fold.Value.Fold);
                        card.Merge(fold.Value.Card);
                        evaluatedDays++;
                        continue;
                    }

                    var training = prior.SelectMany(d => byDay[d]).ToList();
                    double cost = CallCost(training, prior.Count, s);
                    var model = new PatternAnalogPredictor(s.Window, s.Horizon, s.IncludeVolume, seed: s.Seed, entry: s.Entry);
                    // Labels resolve inside their own session, exactly as D's calls are scored; a label
                    // spanning the overnight gap would teach the gap, not the pattern.
                    int sessionStart = 0;
                    foreach (var d in prior)
                    {
                        int sessionEnd = sessionStart + byDay[d].Count;
                        model.Train(training, sessionStart, sessionEnd, labelLimit: sessionEnd);
                        sessionStart = sessionEnd;
                    }
                    if (model.Count == 0) { skipped++; continue; }

                    // Window context may reach back into earlier sessions (already past); calls and outcomes stay inside D.
                    var tail = training.Skip(Math.Max(0, training.Count - (s.Window - 1))).ToList();
                    var test = tail.Concat(dayBars).ToList();
                    int firstInDay = tail.Count;
                    var dayCard = new DirectionalScorecard();
                    for (int end = Math.Max(firstInDay, s.Window - 1); end + s.Horizon < test.Count; end += s.Horizon)
                    {
                        var vote = model.Predict(test, end, s.K, s.MinSimilarity, s.MinNeighbors, s.MinAgreement, s.DeadBand);
                        double fwd = ForwardReturnLabeler.ForwardReturn(test, end, s.Horizon, s.Entry);
                        if (double.IsNaN(fwd)) continue;
                        dayCard.Record(vote.Call, fwd, cost);
                        points++;
                        if (fwd > s.DeadBand) ups++;
                        else if (fwd < -s.DeadBand) downs++;
                    }
                    folds.Add(new WalkForwardFold(symbol, day, prior[0], prior[^1], training[^1].StartUtc, dayBars[0].StartUtc, model.Count, dayCard.Calls, dayCard.Hits, dayCard.Abstentions));
                    card.Merge(dayCard);
                    evaluatedDays++;
                }

                overall.Merge(card);
                if (evaluatedDays > 0)
                    perSymbol.Add(new WalkForwardSymbolScore(symbol, evaluatedDays, card.Calls, card.Hits, card.HitRate, card.PValue, card.MeanEdge));
            }
        }
        return new WalkForwardReport(overall, perSymbol, folds, points, ups, downs, skipped, new FeedMix(sources));
    }

    /// <summary>Round-trip cost for calls on one (symbol, day), from bars before that day only.</summary>
    internal static double CallCost(IReadOnlyList<Candle> priorBars, int sessions, WalkForwardSettings s) =>
        s.ModelCosts ? TradingCosts.Estimate(priorBars, sessions, s.Shares, s.Costs).RoundTrip : s.RoundTripCost;

    /// <summary>
    /// Leader model for one (symbol, day D): leaders are discovered only from the prior sessions'
    /// bars (every candidate bar is cut at D's first bar), then each call follows the sign of the
    /// leaders' influence-weighted move over their bars that closed by the call bar's close.
    /// TrainingWindows records how many leaders were found.
    /// </summary>
    private static (WalkForwardFold Fold, DirectionalScorecard Card)? EvaluateLeaders(string symbol, DateOnly day, List<DateOnly> prior,
        Dictionary<DateOnly, List<Candle>> byDay, List<Candle> dayBars, IReadOnlyDictionary<string, IReadOnlyList<Candle>> pool,
        WalkForwardSettings s, ref int points, ref int ups, ref int downs)
    {
        var dayStartUtc = dayBars[0].StartUtc;
        var priorSet = prior.ToHashSet();
        var history = new Dictionary<string, IReadOnlyList<Candle>>(StringComparer.Ordinal)
        {
            [symbol] = prior.SelectMany(d => byDay[d]).ToList(),
        };
        foreach (var (candidate, candles) in pool)
            if (!candidate.Equals(symbol, StringComparison.Ordinal))
                history[candidate] = candles.Where(c => c.EndUtc <= dayStartUtc && priorSet.Contains(EvidenceSupport.DateEt(c))).ToList();

        double cost = CallCost(history[symbol], prior.Count, s);
        var links = LeaderDiscovery.Discover(symbol, history, s.LeaderMaxHops, minInfluence: s.LeaderMinInfluence, edge: s.LeaderEdge, cleanRmt: s.LeaderCleanRmt);
        if (links.Count == 0) return null;

        var today = links.ToDictionary(l => l.Symbol,
            l => (IReadOnlyList<Candle>)pool[l.Symbol].Where(c => EvidenceSupport.DateEt(c) == day).ToList(), StringComparer.Ordinal);
        var dayCard = new DirectionalScorecard();
        for (int end = s.LeaderLookback; end + s.Horizon < dayBars.Count; end += s.Horizon)
        {
            var asOf = dayBars[end].EndUtc;
            var move = LeaderSignal.WeightedMove(
                links.Select(l => (l.Hops, l.Influence, LeaderSignal.RecentMove(today[l.Symbol], asOf, s.LeaderLookback))),
                s.LeaderMaxHops, s.LeaderMinInfluence);
            var call = move is { } m ? m > s.DeadBand ? Direction.Up : m < -s.DeadBand ? Direction.Down : Direction.Flat : Direction.Flat;
            double fwd = ForwardReturnLabeler.ForwardReturn(dayBars, end, s.Horizon, s.Entry);
            if (double.IsNaN(fwd)) continue;
            dayCard.Record(call, fwd, cost);
            points++;
            if (fwd > s.DeadBand) ups++;
            else if (fwd < -s.DeadBand) downs++;
        }
        var lastTraining = history[symbol];
        return (new WalkForwardFold(symbol, day, prior[0], prior[^1], lastTraining[^1].StartUtc, dayStartUtc, links.Count,
            dayCard.Calls, dayCard.Hits, dayCard.Abstentions), dayCard);
    }
}
