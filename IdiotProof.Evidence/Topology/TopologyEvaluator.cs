namespace IdiotProof.Evidence.Topology;

/// <summary>One trading session's closes, in time order.</summary>
public sealed record SessionSeries(DateOnly Day, IReadOnlyList<double> Closes);

/// <summary>How one topology feature fared: its best training bucket, re-measured on held-out sessions.</summary>
public sealed record TopologyFeatureVerdict(
    string Feature,
    int Bucket,
    int Buckets,
    bool UpBucket,
    double TrainRate,
    int TrainSamples,
    double TrainLift,
    int HoldoutHits,
    int HoldoutSamples,
    double HoldoutRate,
    double HoldoutLow,
    double HoldoutHigh,
    double HoldoutBaseRate,
    double HoldoutLift,
    double BinomialP,
    ClusteredResult Clustered,
    double Alpha,
    bool Promoted);

public sealed record TopologyEvaluation(
    int TrainSessions,
    int HoldoutSessions,
    int TrainSamples,
    int HoldoutSamples,
    double TrainBaseRate,
    double HoldoutBaseRate,
    IReadOnlyList<TopologyFeatureVerdict> Features);

/// <summary>
/// Decides whether any topology feature predicts the next move, the only way the toolkit allows:
/// quantile buckets and per-bucket up-rates are learned on the earlier sessions; each feature's most
/// extreme training bucket (highest or lowest lift) is then re-scored on later, held-out sessions
/// against that period's own base rate. A feature is promoted only when the holdout edge survives a
/// session-clustered test at α divided by the number of features tried.
/// </summary>
public static class TopologyEvaluator
{
    public static TopologyEvaluation Evaluate(IReadOnlyList<SessionSeries> sessions, TopologySettings settings,
        int horizon = 5, int buckets = 4, double holdoutFraction = 0.3, int minBucketSamples = 30, double alpha = 0.05, int seed = 1337)
    {
        if (horizon < 1) throw new ArgumentOutOfRangeException(nameof(horizon));
        // Split by trading day, not by session, so symbols sharing a day never straddle train and holdout.
        var days = sessions.Select(s => s.Day).Distinct().OrderBy(d => d).ToList();
        int holdDays = days.Count < 2 ? 0 : Math.Max(1, (int)Math.Round(days.Count * holdoutFraction));
        var firstHoldDay = holdDays == 0 ? DateOnly.MaxValue : days[days.Count - holdDays];
        var train = sessions.Where(s => s.Day < firstHoldDay).ToList();
        var hold = sessions.Where(s => s.Day >= firstHoldDay).ToList();

        var trainRows = Rows(train, settings, horizon);
        var holdRows = Rows(hold, settings, horizon);
        double trainBase = trainRows.Count == 0 ? double.NaN : trainRows.Count(r => r.Up) / (double)trainRows.Count;
        double holdBase = holdRows.Count == 0 ? double.NaN : holdRows.Count(r => r.Up) / (double)holdRows.Count;
        double perFeatureAlpha = alpha / TopologyFeatures.Names.Length;

        var verdicts = new List<TopologyFeatureVerdict>();
        foreach (var name in TopologyFeatures.Names)
        {
            if (trainRows.Count == 0) break;
            var binner = new QuantileBinner(trainRows.Select(r => TopologyFeatures.Value(r.Reading, name)), buckets);
            var table = new ConditionalOutcomeTable<int>();
            foreach (var r in trainRows) table.Add(binner.Bin(TopologyFeatures.Value(r.Reading, name)), r.Up);

            var candidates = table.All().Where(c => c.Rate.Samples >= minBucketSamples).ToList();
            if (candidates.Count == 0) continue;
            var pick = candidates.OrderByDescending(c => Math.Abs(c.Rate.Probability - trainBase)).First();
            bool upBucket = pick.Rate.Probability >= trainBase;

            // Re-score on holdout: a "hit" is the move the training bucket said was more likely.
            var tallies = new Dictionary<DateOnly, (int Calls, int Hits)>();
            int hits = 0, n = 0;
            foreach (var r in holdRows)
            {
                if (binner.Bin(TopologyFeatures.Value(r.Reading, name)) != pick.Key) continue;
                bool hit = r.Up == upBucket;
                n++;
                if (hit) hits++;
                tallies.TryGetValue(r.Day, out var t);
                tallies[r.Day] = (t.Calls + 1, t.Hits + (hit ? 1 : 0));
            }
            double nullRate = upBucket ? holdBase : 1 - holdBase;
            double rate = n == 0 ? double.NaN : (double)hits / n;
            var (lo, hi) = EvidenceStats.WilsonInterval(hits, n);
            double binomP = n == 0 || double.IsNaN(nullRate) ? 1 : EvidenceStats.BinomialUpperTail(hits, n, nullRate);
            var clustered = ClusteredSignificance.Evaluate(
                tallies.Select(kv => new DayTally(kv.Key, kv.Value.Calls, kv.Value.Hits)).ToList(),
                double.IsNaN(nullRate) ? 0.5 : nullRate, seed: seed);
            bool promoted = n > 0 && binomP < perFeatureAlpha && clustered.IsSignificant(perFeatureAlpha);

            verdicts.Add(new TopologyFeatureVerdict(name, pick.Key, binner.Bins, upBucket, pick.Rate.Probability, pick.Rate.Samples,
                trainBase > 0 ? pick.Rate.Probability / trainBase : double.NaN, hits, n, rate, lo, hi, holdBase,
                nullRate > 0 && n > 0 ? rate / nullRate : double.NaN, binomP, clustered, perFeatureAlpha, promoted));
        }
        return new TopologyEvaluation(train.Count, hold.Count, trainRows.Count, holdRows.Count, trainBase, holdBase, verdicts);
    }

    private sealed record Row(DateOnly Day, TopologyReading Reading, bool Up);

    /// <summary>Readings spaced by the horizon inside each session, so no two labels overlap and none crosses the overnight gap.</summary>
    private static List<Row> Rows(IEnumerable<SessionSeries> sessions, TopologySettings settings, int horizon)
    {
        var rows = new List<Row>();
        foreach (var s in sessions)
            for (int i = settings.Window - 1; i + horizon < s.Closes.Count; i += horizon)
            {
                var reading = TopologyFeatures.At(s.Closes, i, settings);
                if (reading is null) continue;
                double entry = s.Closes[i], exit = s.Closes[i + horizon];
                if (entry <= 0 || exit == entry) continue;
                rows.Add(new Row(s.Day, reading, exit > entry));
            }
        return rows;
    }
}
