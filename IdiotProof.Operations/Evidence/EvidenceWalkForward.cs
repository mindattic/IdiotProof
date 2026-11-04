using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
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
    public bool IncludeVolume { get; init; }
    public EvidenceSession Session { get; init; } = EvidenceSession.Rth;
    public int Seed { get; init; } = 1337;
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
    int SkippedSymbolDays)
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

        foreach (var chunk in plan.Where(p => p.Value.Count > 0).OrderBy(p => p.Key, StringComparer.Ordinal).Chunk(FetchChunk))
        {
            ct.ThrowIfCancellationRequested();
            var from = chunk.Min(p => p.Value.Min).AddDays(-(s.TrainDays * 3 / 2 + 7));
            var to = chunk.Max(p => p.Value.Max);
            var bars = await store.GetBarsForSymbolsAsync(chunk.Select(p => p.Key), from, to, BarTimeframe.Minute1, ct: ct);

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

                    var training = prior.SelectMany(d => byDay[d]).ToList();
                    var model = new PatternAnalogPredictor(s.Window, s.Horizon, s.IncludeVolume, seed: s.Seed);
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
                        double fwd = (double)test[end + s.Horizon].Close / (double)test[end].Close - 1;
                        dayCard.Record(vote.Call, fwd);
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
        return new WalkForwardReport(overall, perSymbol, folds, points, ups, downs, skipped);
    }
}
